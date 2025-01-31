using Sandbox;

namespace TTT;

[Category( "Roles" )]
[ClassName( "ttt_role_detective" )]
[Title( "Detective" )]
public class Detective : Role
{
	public static Clothing Hat;

	public override void OnSelect( Player player )
	{
		base.OnSelect( player );

		if ( !Host.IsServer )
			return;

		player.IsRoleKnown = true;
		player.Inventory.Add( new DNAScanner() );
		player.Perks.Add( new Armor() );

		player.ClothingContainer.Toggle( Hat );
		player.ClothingContainer.DressEntity( player );
		player.ClothingContainer.Toggle( Hat );
	}
}
