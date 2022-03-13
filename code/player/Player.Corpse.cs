using Sandbox;
using System.Collections.Generic;

namespace TTT;

public partial class Corpse : ModelEntity, IEntityHint, IUse
{
	public long PlayerId { get; private set; }
	public string PlayerName { get; private set; }
	public Player DeadPlayer { get; private set; }
	public Player Confirmer { get; private set; }
	public DamageInfo KillInfo { get; set; }
	public List<Particles> Ropes = new();
	public List<PhysicsJoint> RopeSprings = new();
	public CarriableInfo KillerWeapon { get; private set; }
	public bool WasHeadshot => GetHitboxGroup( KillInfo.HitboxIndex ) == (int)HitboxGroup.Head;
	public float Distance { get; private set; } = 0f;
	public float KilledTime { get; private set; }
	public string[] Perks { get; set; }

	// We need this so we don't send information to players multiple times
	private readonly HashSet<int> _playersWhoGotSentInfo = new();

	public override void Spawn()
	{
		base.Spawn();

		MoveType = MoveType.Physics;
		UsePhysicsCollision = true;

		SetInteractsAs( CollisionLayer.Debris );
		SetInteractsWith( CollisionLayer.WORLD_GEOMETRY );
		SetInteractsExclude( CollisionLayer.Player );

		KilledTime = Time.Now;
	}

	public void CopyFrom( Player player )
	{
		DeadPlayer = player;
		PlayerName = player.Client.Name;
		PlayerId = player.Client.PlayerId;
		KillInfo = player.LastDamageInfo;
		KillerWeapon = Asset.GetInfo<CarriableInfo>( KillInfo.Weapon );
		Distance = player.LastDistanceToAttacker;
		player.Corpse = this;

		SetModel( player.GetModelName() );
		TakeDecalsFrom( player );

		this.CopyBonesFrom( player );
		this.SetRagdollVelocityFrom( player );

		List<Entity> attachedEnts = new();
		foreach ( Entity child in player.Children )
		{
			if ( child is BaseClothing e )
			{
				var model = e.GetModelName();
				var clothing = new ModelEntity();
				clothing.RenderColor = e.RenderColor;
				clothing.SetModel( model );
				clothing.SetParent( this, true );
			}
		}

		Perks = new string[DeadPlayer.Perks.Count];
		for ( int i = 0; i < DeadPlayer.Perks.Count; i++ )
		{
			Perks[i] = DeadPlayer.Perks.Get( i ).Info.Title;
		}

		foreach ( Entity entity in attachedEnts )
		{
			entity.SetParent( this, false );
		}
	}

	public void ApplyForceToBone( Vector3 force, int forceBone )
	{
		PhysicsGroup.AddVelocity( force );

		if ( forceBone < 0 )
			return;

		PhysicsBody corpse = GetBonePhysicsBody( forceBone );

		if ( corpse != null )
			corpse.ApplyForce( force * 1000 );
		else
			PhysicsGroup.AddVelocity( force );
	}

	public void ClearAttachments()
	{
		foreach ( Particles rope in Ropes )
		{
			rope.Destroy( true );
		}

		foreach ( PhysicsJoint spring in RopeSprings )
		{
			spring.Remove();
		}

		Ropes.Clear();
		RopeSprings.Clear();
	}

	protected override void OnDestroy()
	{
		ClearAttachments();
	}

	public void SendInfo( To to )
	{
		Host.AssertServer();

		foreach ( var client in to )
		{
			// Don't send general data to players who covert searched
			if ( _playersWhoGotSentInfo.Contains( client.Pawn.NetworkIdent ) )
				continue;

			_playersWhoGotSentInfo.Add( client.Pawn.NetworkIdent );

			GetKillInfo( To.Single( client ), KillInfo.Attacker, KillerWeapon?.Id ?? 0, KillInfo.HitboxIndex, KillInfo.Damage, KillInfo.Flags, Distance, KilledTime );
			GetPlayerData( To.Single( client ), DeadPlayer, PlayerId, PlayerName );
		}
	}

	public void Search( Player searcher )
	{
		Host.AssertServer();

		int credits = 0;

		if ( DeadPlayer.Credits > 0 && searcher.IsValid() && searcher.IsAlive() && searcher.Role.Info.RetrieveCredits )
		{
			searcher.Credits += DeadPlayer.Credits;
			credits = DeadPlayer.Credits;
			DeadPlayer.Credits = 0;
			DeadPlayer.CorpseCredits = DeadPlayer.Credits;
		}

		DeadPlayer.SendRoleToClient( To.Single( searcher ) );
		SendInfo( To.Single( searcher ) );
		ClientSearch( To.Single( searcher ), credits );
	}

	[ClientRpc]
	private void ClientSearch( int credits = 0 )
	{
		DeadPlayer.IsMissingInAction = true;

		if ( credits <= 0 )
			return;

		UI.InfoFeed.Instance?.AddClientEntry
		(
			Local.Client,
			$"found $ {credits} credits!"
		);
	}

	[ClientRpc]
	private void GetKillInfo( Entity attacker, int weaponId, int hitboxIndex, float damage, DamageFlags damageFlag, float distance, float killedTime )
	{
		var info = new DamageInfo()
			.WithAttacker( attacker )
			.WithHitbox( hitboxIndex )
			.WithFlag( damageFlag );

		info.Damage = damage;
		KillInfo = info;
		LastAttacker = info.Attacker;
		KillerWeapon = Asset.FromId<CarriableInfo>( weaponId );
		Distance = distance;
		KilledTime = killedTime;
	}

	[ClientRpc]
	private void GetPlayerData( Player deadPlayer, long playerId, string name )
	{
		DeadPlayer = deadPlayer;
		PlayerId = playerId;
		PlayerName = name;
		DeadPlayer.Corpse = this;
	}

	public float HintDistance => Player.INTERACT_DISTANCE;

	// DeadPlayer is only sent to client once the body is confirmed, therefore check if null.
	public string TextOnTick => DeadPlayer == null || !DeadPlayer.IsConfirmedDead ? $"Hold {Input.GetButtonOrigin( InputButton.Use ).ToUpper()} to identify the corpse"
																				  : $"Hold {Input.GetButtonOrigin( InputButton.Use ).ToUpper()} to inspect {PlayerName}";

	public string SubTextOnTick => DeadPlayer == null || !DeadPlayer.IsConfirmedDead ? $"Hold {Input.GetButtonOrigin( InputButton.Use ).ToUpper()} + {Input.GetButtonOrigin( InputButton.Run ).ToUpper()} to search covertly" : "";

	bool IEntityHint.CanHint( Player client ) => true;

	UI.EntityHintPanel IEntityHint.DisplayHint( Player client )
	{
		return new UI.Hint( TextOnTick, SubTextOnTick );
	}

	void IEntityHint.Tick( Player player )
	{
		if ( !Input.Down( InputButton.Use ) )
			UI.FullScreenHintMenu.Instance?.Close();
		else if ( DeadPlayer.IsValid() && !UI.FullScreenHintMenu.Instance.IsOpen )
			UI.FullScreenHintMenu.Instance?.Open( new UI.InspectMenu( this ) );
	}

	bool IUse.OnUse( Entity user )
	{
		return true;
	}

	bool IUse.IsUsable( Entity user )
	{
		// For now, let's not let people inspect outside of InProgressRound.
		// we should probably create an "empty" corpse instead.
		if ( Game.Current.Round is not InProgressRound )
			return false;

		var player = user as Player;

		if ( !DeadPlayer.IsConfirmedDead )
		{
			if ( !_playersWhoGotSentInfo.Contains( player.NetworkIdent ) )
				Search( player );

			if ( player.IsAlive() && !Input.Down( InputButton.Run ) )
			{
				DeadPlayer.Confirmer = player;
				DeadPlayer.Confirm();
			}
		}

		return true;
	}
}
