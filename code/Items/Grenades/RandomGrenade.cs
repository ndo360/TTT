using Sandbox;
using System;
using System.Collections.Generic;

namespace TTT;

[Library( "ttt_grenade_random" )]
public class RandomGrenade : Entity
{
	private static readonly List<Type> _cachedGrenadeTypes = new();
	private const int GRENADE_DISTANCE_UP = 4;

	public override void Spawn()
	{
		base.Spawn();

		Transmit = TransmitType.Never;

		if ( _cachedGrenadeTypes.IsNullOrEmpty() )
		{
			var grenades = Library.GetAll<Grenade>();
			foreach ( var grenadeType in grenades )
			{
				var grenadeInfo = Asset.GetInfo<CarriableInfo>( Library.GetAttribute( grenadeType ).Name );
				if ( grenadeInfo is not null && grenadeInfo.Spawnable )
					_cachedGrenadeTypes.Add( grenadeType );
			}
		}

		Activate( _cachedGrenadeTypes );
	}

	public void Activate( List<Type> grenadeTypes )
	{
		if ( grenadeTypes.Count <= 0 )
			return;

		var grenade = Library.Create<Grenade>( Rand.FromList( grenadeTypes ) );
		if ( grenade is null )
			return;

		grenade.Position = Position + (Vector3.Up * GRENADE_DISTANCE_UP);
		grenade.Rotation = Rotation;
	}
}
