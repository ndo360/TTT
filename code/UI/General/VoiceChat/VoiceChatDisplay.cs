using Sandbox;
using Sandbox.UI;
using System.Linq;

namespace TTT.UI;

[UseTemplate]
public class VoiceChatDisplay : Panel
{
	public static VoiceChatDisplay Instance { get; private set; }

	public VoiceChatDisplay() => Instance = this;

	public void OnVoicePlayed( Client client )
	{
		var entry = ChildrenOfType<VoiceChatEntry>().FirstOrDefault( x => x.Friend.Id == client.PlayerId ) ?? new VoiceChatEntry( this, client );
		entry.Update( client.VoiceLevel );
	}

	public override void Tick()
	{
		if ( Voice.IsRecording )
			OnVoicePlayed( Local.Client );
	}
}
