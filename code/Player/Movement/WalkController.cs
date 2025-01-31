using System;
using Sandbox;

namespace TTT;

public partial class WalkController : PawnController
{
	[Net] public float WalkSpeed { get; set; } = 110f;
	[Net] public float DefaultSpeed { get; set; } = 230f;
	[Net] public float Acceleration { get; set; } = 8.0f;
	[Net] public float AirAcceleration { get; set; } = 65f;
	[Net] public float FallSoundZ { get; set; } = -30.0f;
	[Net] public float GroundFriction { get; set; } = 5.0f;
	[Net] public float StopSpeed { get; set; } = 105f;
	[Net] public float Size { get; set; } = 20.0f;
	[Net] public float DistEpsilon { get; set; } = 0.03125f;
	[Net] public float GroundAngle { get; set; } = 46.0f;
	[Net] public float Bounce { get; set; } = 0.0f;
	[Net] public float MoveFriction { get; set; } = 1.0f;
	[Net] public float StepSize { get; set; } = 18.0f;
	[Net] public float MaxNonJumpVelocity { get; set; } = 140.0f;
	[Net] public float BodyGirth { get; set; } = 32.0f;
	[Net] public float BodyHeight { get; set; } = 72.0f;
	[Net] public float EyeHeight { get; set; } = 64.0f;
	[Net] public float Gravity { get; set; } = 800.0f;
	[Net] public float AirControl { get; set; } = 30.0f;
	[Net] public bool AutoJump { get; set; } = false;
	[Net, Predicted] public bool Momentum { get; set; }
	[Net, Predicted] public TimeSince TimeSinceWaterJump { get; set; }
	public bool Swimming { get; set; } = false;

	public Duck Duck;
	public Unstuck Unstuck;

	private const float FallDamageThreshold = 650f;
	private const float FallDamageScale = 0.33f;
	private Vector3 _lastBaseVelocity;
	private float _surfaceFriction;

	public WalkController()
	{
		Duck = new Duck( this );
		Unstuck = new Unstuck( this );
	}

	public override void FrameSimulate()
	{
		base.FrameSimulate();

		EyeRotation = Input.Rotation;
	}

	public override void Simulate()
	{
		_lastBaseVelocity = BaseVelocity;
		ApplyMomentum();
		BaseSimulate();
	}

	private float GetWishSpeed()
	{
		var ws = Duck.GetWishSpeed();
		if ( ws >= 0 )
			return ws;

		if ( Input.Down( InputButton.Run ) )
			return WalkSpeed;

		return DefaultSpeed;
	}

	private void WalkMove()
	{
		var wishdir = WishVelocity.Normal;
		var wishspeed = WishVelocity.Length;

		WishVelocity = WishVelocity.WithZ( 0 );
		WishVelocity = WishVelocity.Normal * wishspeed;

		Velocity = Velocity.WithZ( 0 );
		Accelerate( wishdir, wishspeed, 0, Acceleration );
		Velocity = Velocity.WithZ( 0 );
		Velocity += BaseVelocity;

		try
		{
			if ( Velocity.Length < 1.0f )
			{
				Velocity = Vector3.Zero;
				return;
			}

			var dest = (Position + Velocity * Time.Delta).WithZ( Position.z );
			var pm = TraceBBox( Position, dest );

			if ( pm.Fraction == 1 )
			{
				Position = pm.EndPosition;
				StayOnGround();
				return;
			}

			StepMove();
		}
		finally
		{
			Velocity -= BaseVelocity;
		}

		StayOnGround();
	}

	private void StepMove()
	{
		var mover = new MoveHelper( Position, Velocity );
		mover.Trace = mover.Trace.Size( _mins, _maxs ).Ignore( Pawn );
		mover.MaxStandableAngle = GroundAngle;

		mover.TryMoveWithStep( Time.Delta, StepSize );

		Position = mover.Position;
		Velocity = mover.Velocity;
	}

	private void Move()
	{
		var mover = new MoveHelper( Position, Velocity );
		mover.Trace = mover.Trace.Size( _mins, _maxs ).Ignore( Pawn );
		mover.MaxStandableAngle = GroundAngle;

		mover.TryMove( Time.Delta );

		Position = mover.Position;
		Velocity = mover.Velocity;
	}

	/// <summary>
	/// Add our wish direction and speed onto our velocity
	/// </summary>
	private void Accelerate( Vector3 wishdir, float wishspeed, float speedLimit, float acceleration )
	{
		// This gets overridden because some games (CSPort) want to allow dead (observer) players
		// to be able to move around.
		// if ( !CanAccelerate() )
		//     return;

		if ( speedLimit > 0 && wishspeed > speedLimit )
			wishspeed = speedLimit;

		// See if we are changing direction a bit
		var currentspeed = Velocity.Dot( wishdir );

		// Reduce wishspeed by the amount of veer.
		var addspeed = wishspeed - currentspeed;

		// If not going to add any speed, done.
		if ( addspeed <= 0 )
			return;

		// Determine amount of acceleration.
		var accelspeed = acceleration * Time.Delta * wishspeed * _surfaceFriction;

		// Cap at addspeed
		if ( accelspeed > addspeed )
			accelspeed = addspeed;

		Velocity += wishdir * accelspeed;
	}

	/// <summary>
	/// Remove ground friction from velocity
	/// </summary>
	private void ApplyFriction( float frictionAmount = 1.0f )
	{
		// If we are in water jump cycle, don't apply friction
		//if ( player->m_flWaterJumpTime )
		//   return;

		// Not on ground - no friction


		// Calculate speed
		var speed = Velocity.Length;
		if ( speed < 0.1f )
			return;

		// Bleed off some speed, but if we have less than the bleed
		//  threshold, bleed the threshold amount.
		var control = (speed < StopSpeed) ? StopSpeed : speed;

		// Add the amount to the drop amount.
		var drop = control * Time.Delta * frictionAmount;

		// scale the velocity
		var newspeed = speed - drop;
		if ( newspeed < 0 )
			newspeed = 0;

		if ( newspeed != speed )
		{
			newspeed /= speed;
			Velocity *= newspeed;
		}

		// mv->m_outWishVel -= (1.f-newspeed) * mv->m_vecVelocity;
	}

	private void CheckJumpButton()
	{
		if ( Swimming )
		{
			ClearGroundEntity();

			Velocity = Velocity.WithZ( 100 );
			return;
		}

		if ( GroundEntity == null )
			return;

		ClearGroundEntity();

		PreventBhop();

		var flGroundFactor = 1.0f;

		var flMul = 268.3281572999747f * 1.2f;
		var startz = Velocity.z;

		if ( Duck.IsActive )
			flMul *= 0.8f;

		Velocity = Velocity.WithZ( startz + flMul * flGroundFactor );
		Velocity -= new Vector3( 0, 0, Gravity * 0.5f ) * Time.Delta;

		AddEvent( "jump" );
	}

	private void AirMove()
	{
		_surfaceFriction = 1f;

		var wishdir = WishVelocity.Normal;
		var wishspeed = WishVelocity.Length;

		Accelerate( wishdir, wishspeed, AirControl, AirAcceleration );

		Velocity += BaseVelocity;

		Move();

		Velocity -= BaseVelocity;
	}

	private void WaterMove()
	{
		var wishvel = WishVelocity;

		if ( Input.Down( InputButton.Jump ) )
		{
			wishvel[2] += DefaultSpeed;
		}
		else if ( wishvel.IsNearlyZero() )
		{
			wishvel[2] -= 60;
		}
		else
		{
			var upwardMovememnt = Input.Forward * (Rotation * Vector3.Forward).z * 2;
			upwardMovememnt = Math.Clamp( upwardMovememnt, 0f, DefaultSpeed );
			wishvel[2] += Input.Up + upwardMovememnt;
		}

		var speed = Velocity.Length;
		var wishspeed = Math.Min( wishvel.Length, DefaultSpeed ) * 0.8f;
		var wishdir = wishvel.Normal;

		if ( speed > 0 )
		{
			var newspeed = speed - Time.Delta * speed * GroundFriction * _surfaceFriction;
			if ( newspeed < 0.1f )
				newspeed = 0;

			Velocity *= newspeed / speed;
		}

		if ( wishspeed >= 0.1f )
			Accelerate( wishdir, wishspeed, 100, Acceleration );

		Velocity += BaseVelocity;

		Move();

		Velocity -= BaseVelocity;
	}

	private void CategorizePosition( bool bStayOnGround )
	{
		_surfaceFriction = 1.0f;

		// Doing this before we move may introduce a potential latency in water detection, but
		// doing it after can get us stuck on the bottom in water if the amount we move up
		// is less than the 1 pixel 'threshold' we're about to snap to.	Also, we'll call
		// this several times per frame, so we really need to avoid sticking to the bottom of
		// water on each call, and the converse case will correct itself if called twice.
		//CheckWater();

		var point = Position - Vector3.Up * 2;
		var vBumpOrigin = Position;

		//
		//  Shooting up really fast.  Definitely not on ground trimed until ladder shit
		//
		var bMovingUpRapidly = Velocity.z + _lastBaseVelocity.z > MaxNonJumpVelocity;
		var bMoveToEndPos = false;

		if ( GroundEntity != null ) // and not underwater
		{
			bMoveToEndPos = true;
			point.z -= StepSize;
		}
		else if ( bStayOnGround )
		{
			bMoveToEndPos = true;
			point.z -= StepSize;
		}

		if ( bMovingUpRapidly || Swimming ) // or ladder and moving up
		{
			ClearGroundEntity();
			return;
		}

		var pm = TraceBBox( vBumpOrigin, point, 4.0f );

		if ( pm.Entity == null || Vector3.GetAngle( Vector3.Up, pm.Normal ) > GroundAngle )
		{
			ClearGroundEntity();
			bMoveToEndPos = false;

			if ( Velocity.z + _lastBaseVelocity.z > 0 )
				_surfaceFriction = 0.25f;
		}
		else
		{
			UpdateGroundEntity( pm );
		}

		if ( bMoveToEndPos && !pm.StartedSolid && pm.Fraction > 0.0f && pm.Fraction < 1.0f )
		{
			Position = pm.EndPosition;
		}
	}

	private void ApplyMomentum()
	{
		if ( !Momentum )
		{
			Velocity += (0.2f + (Time.Delta * 0.5f)) * BaseVelocity;
			BaseVelocity = Vector3.Zero;
		}

		Momentum = false;
	}

	private void BaseSimulate()
	{
		EyeLocalPosition = Vector3.Up * (EyeHeight * Pawn.Scale);
		UpdateBBox();

		EyeLocalPosition += TraceOffset;
		EyeRotation = Input.Rotation;

		if ( Unstuck.TestAndFix() )
			return;

		CheckLadder();
		Swimming = Pawn.WaterLevel > 0.6f;

		if ( !Swimming && !_isTouchingLadder )
		{
			Velocity -= new Vector3( 0, 0, Gravity * 0.5f ) * Time.Delta;
			Velocity += new Vector3( 0, 0, BaseVelocity.z ) * Time.Delta;

			BaseVelocity = BaseVelocity.WithZ( 0 );
		}

		if ( AutoJump ? Input.Down( InputButton.Jump ) : Input.Pressed( InputButton.Jump ) )
			CheckJumpButton();

		var bStartOnGround = GroundEntity != null;
		if ( bStartOnGround )
		{
			Velocity = Velocity.WithZ( 0 );

			if ( GroundEntity != null )
				ApplyFriction( GroundFriction * _surfaceFriction * 1 );
		}

		WishVelocity = new Vector3( Input.Forward, Input.Left, 0 );
		var inSpeed = WishVelocity.Length.Clamp( 0, 1 );

		if ( !Swimming )
			WishVelocity *= Input.Rotation.Angles().WithPitch( 0 ).ToRotation();
		else
			WishVelocity *= Input.Rotation.Angles().ToRotation();


		if ( !Swimming && !_isTouchingLadder )
			WishVelocity = WishVelocity.WithZ( 0 );

		WishVelocity = WishVelocity.Normal * inSpeed;
		WishVelocity *= GetWishSpeed();

		Duck.PreTick();

		var bStayOnGround = false;
		if ( Swimming )
		{
			if ( Pawn.WaterLevel.AlmostEqual( 0.6f, .05f ) )
				CheckWaterJump();

			WaterMove();
		}
		else if ( _isTouchingLadder )
		{
			SetTag( "climbing" );
			LadderMove();
		}
		else if ( GroundEntity != null )
		{
			bStayOnGround = true;
			WalkMove();
		}
		else
		{
			AirMove();
		}

		CategorizePosition( bStayOnGround );

		if ( !Swimming && !_isTouchingLadder )
			Velocity -= new Vector3( 0, 0, Gravity * 0.5f ) * Time.Delta;

		if ( GroundEntity != null )
			Velocity = Velocity.WithZ( 0 );

		var fallVelocity = -Pawn.Velocity.z;
		if ( GroundEntity is null || fallVelocity <= 0 )
			return;

		if ( fallVelocity > FallDamageThreshold )
		{
			var damage = (MathF.Abs( fallVelocity ) - FallDamageThreshold) * FallDamageScale;

			if ( Host.IsServer )
			{
				Pawn.TakeDamage( new DamageInfo
				{
					Attacker = Pawn,
					Flags = DamageFlags.Fall,
					Force = Vector3.Down * Velocity.Length,
					Damage = damage,
				} );
			}

			Pawn.PlaySound( Strings.FallDamageSound ).SetVolume( (damage * 0.05f).Clamp( 0, 0.5f ) );
		}
	}

	/// <summary>
	/// We have a new ground entity
	/// </summary>
	private void UpdateGroundEntity( TraceResult tr )
	{
		GroundNormal = tr.Normal;

		// VALVE HACKHACK: Scale this to fudge the relationship between vphysics friction values and player friction values.
		// A value of 0.8f feels pretty normal for vphysics, whereas 1.0f is normal for players.
		// This scaling trivially makes them equivalent.  REVISIT if this affects low friction surfaces too much.
		_surfaceFriction = tr.Surface.Friction * 1.25f;
		if ( _surfaceFriction > 1 )
			_surfaceFriction = 1;

		GroundEntity = tr.Entity;

		if ( GroundEntity != null )
			BaseVelocity = GroundEntity.Velocity;
	}

	/// <summary>
	/// We're no longer on the ground, remove it
	/// </summary>
	private void ClearGroundEntity()
	{
		if ( GroundEntity == null )
			return;

		GroundEntity = null;
		GroundNormal = Vector3.Up;
		_surfaceFriction = 1.0f;
	}

	private void CheckWaterJump()
	{
		if ( !Input.Down( InputButton.Jump ) )
			return;

		if ( TimeSinceWaterJump < 2f )
			return;

		if ( Velocity.z < -180 )
			return;

		var fwd = Rotation * Vector3.Forward;
		var flatvelocity = Velocity.WithZ( 0 );
		var curspeed = flatvelocity.Length;
		flatvelocity = flatvelocity.Normal;
		var flatforward = fwd.WithZ( 0 ).Normal;

		// Are we backing into water from steps or something?  If so, don't pop forward
		if ( !curspeed.AlmostEqual( 0f ) && (Vector3.Dot( flatvelocity, flatforward ) < 0f) )
			return;

		var vecstart = Position + (_mins + _maxs) * .5f;
		var vecend = vecstart + flatforward * 24f;

		var tr = TraceBBox( vecstart, vecend, 0f );
		if ( tr.Fraction < 1.0f )
		{
			const float WATERJUMP_HEIGHT = 900f;
			vecstart.z += Position.z + EyeHeight + WATERJUMP_HEIGHT;

			vecend = vecstart + flatforward * 24f;

			tr = TraceBBox( vecstart, vecend );
			if ( tr.Fraction == 1.0f )
			{
				vecstart = vecend;
				vecend.z -= 1024f;
				tr = TraceBBox( vecstart, vecend );
				if ( (tr.Fraction < 1.0f) && (tr.Normal.z >= 0.7f) )
				{
					Velocity = Velocity.WithZ( 356f );
					TimeSinceWaterJump = 0f;
				}
			}
		}
	}

	/// <summary>
	/// Try to keep a walking player on the ground when running down slopes etc
	/// </summary>
	private void StayOnGround()
	{
		var start = Position + Vector3.Up * 2;
		var end = Position + Vector3.Down * StepSize;

		// See how far up we can go without getting stuck
		var trace = TraceBBox( Position, start );
		start = trace.EndPosition;

		// Now trace down from a known safe position
		trace = TraceBBox( start, end );

		if ( trace.Fraction <= 0 )
			return;
		if ( trace.Fraction >= 1 )
			return;
		if ( trace.StartedSolid )
			return;

		if ( Vector3.GetAngle( Vector3.Up, trace.Normal ) > GroundAngle )
			return;

		// This is incredibly hacky. The real problem is that trace returning that strange value we can't network over.
		// float flDelta = fabs( mv->GetAbsOrigin().z - trace.m_vEndPos.z );
		// if ( flDelta > 0.5f * DIST_EPSILON )

		Position = trace.EndPosition;
	}

	/// <summary>
	/// Translated code from CS:GO
	/// </summary>
	private void PreventBhop()
	{
		// Speed at which bunny jumping is limited
		var maxscaledspeed = 1.1f * DefaultSpeed;
		if ( maxscaledspeed <= 0.0f )
			return;

		// Current player speed
		var spd = Velocity.Length;

		if ( spd <= maxscaledspeed )
			return;

		// Apply this cropping fraction to velocity
		var fraction = maxscaledspeed / spd;

		Velocity *= fraction;
	}
}
