using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TTT;

[Library( "ttt_perk_radar", Title = "Radar" )]
public partial class Radar : Perk
{
	public struct RadarPointData
	{
		public Color Color;
		public Vector3 Position;
	}

	public override string ActiveText => $"{Math.Abs( Math.Round( _timeUntilExecution ) )}";
	private readonly float _timeToExecute = 20f;
	private TimeUntil _timeUntilExecution;
	private RadarPointData[] _lastPositions;
	private readonly List<RadarPoint> _cachedPoints = new();
	private readonly Color _defaultRadarColor = Color.FromBytes( 124, 252, 0 );
	private readonly Vector3 _radarPointOffset = Vector3.Up * 45;

	public Radar()
	{
		// We should execute as soon as the perk is equipped.
		_timeUntilExecution = 0;
	}

	protected override void OnActivate()
	{
		base.OnActivate();

		if ( Host.IsClient )
			Local.Hud.AddChild( new RadarDisplay() );
	}

	protected override void OnDeactivate()
	{
		base.OnDeactivate();

		if ( Host.IsClient )
			RadarDisplay.Instance?.Delete();
	}

	public override void Simulate( Player player )
	{
		if ( Math.Round( _timeUntilExecution ) < 0f )
		{
			UpdatePositions( player );
			_timeUntilExecution = _timeToExecute;
		}
	}

	private void UpdatePositions( Player owner )
	{
		if ( Host.IsServer )
		{
			List<RadarPointData> pointData = new();

			foreach ( Player player in Utils.GetAlivePlayers() )
			{
				if ( player.Client.PlayerId == owner.Client.PlayerId )
					continue;

				pointData.Add( new RadarPointData
				{
					Position = player.Position + _radarPointOffset,
					Color = player.Role == owner.Role ? owner.Role.Info.Color : _defaultRadarColor
				} );
			}

			if ( owner.Team != Team.Traitors )
			{
				List<Vector3> decoyPositions = Sandbox.Entity.All.Where( x => x.GetType() == typeof( DecoyEntity ) )?.Select( x => x.Position ).ToList();
				foreach ( Vector3 decoyPosition in decoyPositions )
				{
					pointData.Add( new RadarPointData
					{
						Position = decoyPosition,
						Color = _defaultRadarColor
					} );
				}
			}

			ClientSendRadarPositions( To.Single( owner ), owner, pointData.ToArray() );
		}
		else
		{
			ClearRadarPoints();

			if ( _lastPositions.IsNullOrEmpty() )
				return;

			foreach ( RadarPointData pointData in _lastPositions )
			{
				_cachedPoints.Add( new RadarPoint( pointData ) );
			}
		}
	}

	private void ClearRadarPoints()
	{
		foreach ( RadarPoint radarPoint in _cachedPoints )
		{
			radarPoint.Delete();
		}

		_cachedPoints.Clear();
	}

	[ClientRpc]
	public static void ClientSendRadarPositions( Player player, RadarPointData[] points )
	{
		if ( !player.IsValid() || player != Local.Pawn )
			return;

		Radar radar = player.Perks.Find<Radar>();
		if ( radar == null )
			return;

		radar._lastPositions = points;
		radar.UpdatePositions( player );
	}
}

