using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Sandbox;

namespace TTT;

public enum EventType
{
	Round,
	PlayerTookDamage,
	PlayerKill,
	PlayerSuicide,
	PlayerCorpseFound
}

public class EventInfo
{
	public EventType EventType { get; set; }
	public float Time { get; set; }
	public string Description { get; set; }

	public static byte[] Serialize( EventInfo[] data ) => Encoding.UTF8.GetBytes( JsonSerializer.Serialize( data ) );
	public static EventInfo[] Deserialize( byte[] bytes ) => JsonSerializer.Deserialize<EventInfo[]>( bytes );
}

public static class EventLogger
{
	public static readonly List<EventInfo> Events = new();

	private const string LogFolder = "round-logs";

	private static TimeSince _timeSinceStart;

	private static void LogEvent( EventType eventType, float time, string description )
	{
		EventInfo eventInfo = new()
		{
			EventType = eventType,
			Time = time,
			Description = description
		};

		Events.Add( eventInfo );
	}

	[GameEvent.Round.Start]
	private static void OnRoundStart()
	{
		if ( !Host.IsServer )
			return;

		Events.Clear();

		_timeSinceStart = 0;
		LogEvent( EventType.Round, _timeSinceStart, "The round started." );
	}

	[GameEvent.Round.End]
	private static void OnRoundEnd( Team winningTeam, WinType winType )
	{
		if ( !Host.IsServer )
			return;

		LogEvent( EventType.Round, _timeSinceStart, $"The {winningTeam.GetTitle()} won the round!" );
		WriteEvents();

		UI.GeneralMenu.SendSummaryData( EventInfo.Serialize( Events.ToArray() ) );
	}

	[GameEvent.Player.TookDamage]
	private static void OnPlayerTookDamage( Player player )
	{
		if ( !Host.IsServer )
			return;

		if ( Game.Current.State is not InProgress )
			return;

		var info = player.LastDamage;
		var attacker = info.Attacker;

		if ( attacker is Player && attacker != player )
			LogEvent( EventType.PlayerTookDamage, _timeSinceStart, $"{attacker.Client.Name} did {info.Damage} damage to {player.SteamName}" );
		else
			LogEvent( EventType.PlayerTookDamage, _timeSinceStart, $"{player.SteamName} took {info.Damage} damage." );
	}

	[GameEvent.Player.Killed]
	private static void OnPlayerKilled( Player player )
	{
		if ( !Host.IsServer )
			return;

		if ( Game.Current.State is not InProgress )
			return;

		if ( player.KilledByPlayer )
			LogEvent( EventType.PlayerKill, _timeSinceStart, $"{player.LastAttacker.Client.Name} killed {player.SteamName}" );
		else if ( player.LastDamage.Flags == DamageFlags.Fall )
			LogEvent( EventType.PlayerSuicide, _timeSinceStart, $"{player.SteamName} fell to their death." );
	}

	[GameEvent.Player.CorpseFound]
	private static void OnCorpseFound( Player player )
	{
		if ( !Host.IsServer )
			return;

		LogEvent( EventType.PlayerCorpseFound, _timeSinceStart, $"{player.Corpse.Finder.SteamName} found the corpse of {player.SteamName}" );
	}

	private static void WriteEvents()
	{
		if ( !Game.LoggerEnabled )
			return;

		if ( !FileSystem.Data.DirectoryExists( LogFolder ) )
			FileSystem.Data.CreateDirectory( LogFolder );

		var mapFolderPath = $"{LogFolder}/{DateTime.Now:yyyy-MM-dd} {Global.MapName}";
		if ( !FileSystem.Data.DirectoryExists( mapFolderPath ) )
			FileSystem.Data.CreateDirectory( mapFolderPath );

		var logFilePath = $"{mapFolderPath}/{DateTime.Now:yyyy-MM-dd HH.mm.ss}.txt";
		FileSystem.Data.WriteAllText( logFilePath, GetEventSummary() );
	}

	private static string GetEventSummary()
	{
		var summary = $"{DateTime.Now:yyyy-MM-dd HH.mm.ss} - {Global.MapName}\n";

		for ( var i = 0; i < Events.Count; ++i )
			summary += $"{Events[i].Time.TimerString()} - {Events[i].Description}\n";

		return summary;
	}
}
