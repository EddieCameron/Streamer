using System;
using Streamer;
	
class MainClass
{
	public static void Main ( string[] args )
	{
		// Create twitteraccess object
		TwitterAccess access = new TwitterAccess();
	
		// Get all tweets with a location tag
		QueryLocations world = new QueryLocations( new Coordinates( -180f, -90f ), new Coordinates( 180f, 90f ) );
		access.AddQueryParameter( world );
		
		// and with the word "bieber" in them
		access.AddQueryParameter( new QueryTrack( "bieber" ) );
		
		access.Connect ( false );
		
		while ( true )
		{
			if ( access.tweets.Count > 0 )
			{
				Tweet newTweet = access.tweets.Dequeue();
				Console.WriteLine( newTweet.userName  + ": " + newTweet.status );
			}
		}
		
		// SHOULD ALWAYS CALL DISCONNECT on access object. THIS DOESN"T!!!
	}
}

