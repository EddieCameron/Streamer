/* DecodeStream.cs - Eddie Cameron
 * ------
 * Decodes JSON objects into tweets 
 * ------
 */
using System;
using System.Threading;
using System.Collections;
using System.Collections.Generic;

#if FOR_UNITY
using UnityEngine;
#endif

namespace Streamer
{
	public class DecodeStream 
	{	
		private Queue<string> tweetStringQueue;	// ref to raw Twitter json
		
		private Thread parseThread; 
		public bool isParsingPaused;
		
		public Queue<Tweet> tweets;				// access this to get tweets
		public double curLimit;
		
		// set up with reference to direct queue from TwitterAccess
		public DecodeStream( Queue<string> tweetStringQueue )
		{
			this.tweetStringQueue = tweetStringQueue;
			tweets = new Queue<Tweet>();
			
			parseThread = new Thread( new ThreadStart( Parse ) ); 
			parseThread.Start ();
		}
		
		// stop any JSON procesing
		public void Stop()
		{
			if ( parseThread != null )
				parseThread.Abort ();
		}
		
		// process all tweets in tweetStringQueue
		void Parse()
		{
			while ( true )
			{
				while ( !isParsingPaused && tweetStringQueue.Count > 0 )
				{
					string newTweetStr = tweetStringQueue.Dequeue();
	
					try
					{
						JSONObject newObj = new JSONObject( newTweetStr );
						
						if ( newObj.HasProperty( "text" ) )
						{
							Tweet newTweet = new Tweet( newObj );
							tweets.Enqueue ( newTweet );
						}
						else if ( newObj.HasProperty ( "limit" ) )
							curLimit = newObj.GetProperty( "limit" ).GetProperty ( "track" ).n;						
						else
							Console.WriteLine ( "Unrecognised (valid) JSON object in stream" );
					}
					catch
					{
						Console.WriteLine( "Invalid object in json string" );
					}
				}
				
				Thread.Sleep ( 100 );
			}		
		}
		
		/// <summary>
		/// Remove extra escape chars from the string
		/// </summary>
		/// <returns>
		/// The string.
		/// </returns>
		/// <param name='urlString'>
		/// URL string.
		/// </param>
		public static string CleanString( string toClean )
		{
			return toClean.Replace ( @"\", @"" );
		}
	}
	
	// To be expanded as needed
	public class Tweet
	{
		public string status = "";
		
		private JSONObject user;
		public string userName = "";
		public string fullName = "";		
		public string avatarURL = "";
		
		private JSONObject location;
		public Coordinates[] coords;	// single entry means point, multiple means bounding box
		
		private JSONObject entities;
		public string[] tags = new string[1];
			
		// Make tweet object from JSON
		public Tweet( JSONObject jsonTweet )
		{	
			if ( jsonTweet.type != JSONObject.Type.OBJECT )
				throw new System.Exception( "Non-valid JSON object" );
					
			status = jsonTweet.GetProperty ( "text" ).str;	
			
			user = jsonTweet.GetProperty ( "user" );
			
			JSONObject name = user.GetProperty ( "screen_name" );
			userName = name.str;
			JSONObject fullNameObj = user.GetProperty ( "name" );
			fullName = fullNameObj.str;
			JSONObject avatarObj = user.GetProperty ( "profile_image_url" );
			avatarURL = DecodeStream.CleanString ( avatarObj.str );
			
			location = jsonTweet.GetProperty( "geo" );
			if ( location.type == JSONObject.Type.NULL )
				location = jsonTweet.GetProperty( "coordinates" );
			// to add parsing for 'place' tagged tweets
		
			if ( location.type != JSONObject.Type.NULL )
				coords = GetLocationData( location );
			
			entities = jsonTweet.GetProperty ( "entities" );
			JSONObject hashtags = entities.GetProperty( "hashtags" );
			if ( hashtags.type != JSONObject.Type.NULL && hashtags.props.Count > 0 )
			{
				tags = new string[ hashtags.props.Count ];
				for ( int i = 0; i < tags.Length; i++ )
					tags[i] = hashtags.props[i].GetProperty ( "text" ).str;
			}
			else
				tags = new string [0];
		}
		
		// extract coords from location object
		public static Coordinates[] GetLocationData( JSONObject locationObj )
		{
			try 
			{
				if ( locationObj.HasProperty( "coordinates" ) )
				{		
					List<JSONObject> coords = locationObj.GetProperty ( "coordinates" ).props;
					if ( coords.Count % 2 != 0 )
						throw new System.Exception( "Wrong coordinate setup?" );
					
					Coordinates[] toRet = new Coordinates[ coords.Count / 2 ];
					for ( int i = 0; i < coords.Count; i += 2 )
					{
						toRet[i] = new Coordinates( (float)coords[i+1].n, (float)coords[i].n );
					}
					return toRet;
				}
				
				throw new System.Exception( "wrong geo type" );
				
			} catch ( System.Exception e ) 
			{
				Console.WriteLine( "Location error:" + e.Message );
				return new Coordinates[2];
			}
		}
	}
}