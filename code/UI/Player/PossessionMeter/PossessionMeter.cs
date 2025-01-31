using Sandbox;
using Sandbox.UI;

namespace TTT.UI;

[UseTemplate]
public class PossessionMeter : Panel
{
	private Panel PercentagePanel { get; init; }
	private readonly PropPossession _possession;

	public PossessionMeter( PropPossession possession )
	{
		_possession = possession;
		Local.Hud.AddChild( this );
	}

	public override void Tick()
	{
		var punchesFraction = (float)_possession.Punches / PropPossession.MaxPunches;
		PercentagePanel.Style.Width = Length.Fraction( punchesFraction );
	}
}
