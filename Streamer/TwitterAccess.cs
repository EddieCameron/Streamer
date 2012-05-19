/*
 * TwitterAccess.cs - Eddie Cameron
 * -------
 * Controls access to the Twitter API for your .Net app
 * 
 * Make sure the OAuth tokens/secrets are set up, or provide your username and password
 * 
 * On a new TwitterAccess object, provide some QueryParameters, connect, and any matching tweets will show up in the "tweets" queue
 * -------
 */

using System;
using System.Net;
using System.IO;
using System.Threading;
using System.Text;
using System.Collections.Generic;
#if FOR_UNITY
using UnityEngine;
#endif

namespace Streamer
{
	public class TwitterAccess
	{
		private const string streamFilterURL = "https://stream.twitter.com/1/statuses/filter.json";
		private const string streamSampleURL = "https://stream.twitter.com/1/statuses/sample.json";
		
		// for the streaming API, you can use basic authentication...for now...
		private string username;
		private string password;
		OAuth oAuth;
		
		public enum ApiMethod{ StreamFilter, StreamSample };
		public ApiMethod apiMethod = TwitterAccess.ApiMethod.StreamFilter;
		private Dictionary<string, TwitterQuery> queries;
		
		private Queue<string> tweetStringQueue;	// this holds tweets as they arrive
		private int maxInQueue = 2048;
		
		private DecodeStream tweetParser;
		public Queue<Tweet> tweets;		// this holds processed tweets
		
		// network backoff times
		int networkErrors = 0;
		int httpRetryTime = 10000;
		
		// information about the API connection
		private HttpWebResponse currentResponse;
		private Thread currentConnection;
		private StreamReader currentReader;
		private bool connectionIsOpen = false;
		
		private Thread connectingThread;
		private WebRequest connectingRequest;
		
		// URL encode strings, since Twitter doesn't like .Net URL encoding
		public static string UrlEncode( string toEncode ) 
		{
	        StringBuilder toRet = new StringBuilder();
			string acceptedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~";
			foreach (char c in toEncode ) 
			{
	            if ( acceptedChars.IndexOf( c ) != -1 )
	                toRet.Append( c );
	            else
	                toRet.Append('%' + string.Format( "{0:X2}", (int)c ) );
	        }
	
	        return toRet.ToString();
	    }
		
		public TwitterAccess()
		{
			tweetStringQueue = new Queue<string>();
			queries = new Dictionary<string,TwitterQuery>();
			
			oAuth = new OAuth();
			
			tweetParser = new DecodeStream( tweetStringQueue );
			tweets = tweetParser.tweets;
			tweetParser.isParsingPaused = true;
		}		
		
		// Add and remove parameters (to add more)
		public void AddQueryParameter( TwitterQuery query )
		{
			if ( queries.ContainsKey( query.GetKey() ) )
				queries[query.GetKey()].MergeQuery( query );
			else
				queries.Add( query.GetKey(), query );
		}
		
		public void RemoveQueryParameter( TwitterQuery query )
		{
			if ( queries.ContainsKey( query.GetKey() ) )
			{
				TwitterQuery inQueries = queries[query.GetKey() ];
				inQueries.RemoveQuery ( query );
				if ( inQueries.GetParameter() == "" )
					queries.Remove( query.GetKey() );
			}
		}
		
		
		// For the streaming API, you can provide a user/password instead of OAuthing
		public void BasicAuthUserPassword( string username, string password )
		{
			this.username = username;
			this.password = password;
		}
		
		public string GetOAuthString()
		{
			return oAuth.GetUserAuth ();
		}
		
		/// <summary>
		/// Tries to authorise with saved OAuth credentials
		/// </summary>
		/// <returns>
		/// If auth was successful
		/// </returns>
		/// <param name='savedAuth'>
		/// String from previous GetOAuthString call
		/// </param>
		public bool OAuthWithString( string savedAuth )
		{
			return oAuth.AuthoriseFromSave ( savedAuth );
		}
		
		public bool IsOAuthed()
		{
			return oAuth.status == OAuthStatus.Authorised;
		}
		
		public void GetOAuthURL( Action<string> receiveUrlCallback )
		{
			oAuth.StartOAuthRequest ( "https://api.twitter.com/oauth/request_token", "https://api.twitter.com/oauth/authorize", receiveUrlCallback );
		}
		
		/// <summary>
		/// Call after user has entered passcode from twitter.com to authorise this app
		/// </summary>
		/// <param name='passcode'>
		/// Passcode. User can get at url given back by StartOAuthRequest
		/// </param>
		public void GetUserTokens( string passcode )
		{
			oAuth.GetAccessTokens( "https://api.twitter.com/oauth/access_token", passcode );
		}
		
		/// /// <summary>
		/// Connect with preset parameters
		/// </summary>
		/// <param name='useBasicAuth'>
		/// Use basic auth(streaming only) CAUTION: Twitter may deprecate this soon
		/// </param>
		public void Connect( bool useBasicAuth )
		{
			// if attempting connection, cancel
			if ( connectingThread != null )
				connectingThread.Abort ();
			
			// translate parameters to string values
			string[,] parameterStrings = new string[queries.Count,2];
			StringBuilder postString = new StringBuilder();
			
			if ( queries.Count > 0 )
			{
				int i = 0;
				foreach ( TwitterQuery query in queries.Values )
				{
					string key = UrlEncode( query.GetKey() );
					string parameter = UrlEncode ( query.GetParameter() );
					parameterStrings[i,0] = key;
					parameterStrings[i,1] = parameter;
					i++;
					
					postString.Append ( key + "=" + parameter + "&" );
				}
				postString = postString.Remove ( postString.Length - 1, 1 );
			}
			
			// make and authorise webrequest
			string url = queries.Count == 0 ? streamSampleURL : streamFilterURL;
			connectingRequest = WebRequest.Create( url );
			
			if ( useBasicAuth )
				connectingRequest.Credentials = new NetworkCredential( username, password );
			else if ( IsOAuthed() )
				connectingRequest.Headers.Add ( HttpRequestHeader.Authorization, oAuth.AuthoriseRequest( parameterStrings, url ) );
			else
			{
	#if FOR_UNITY
				Debug.Log ( "Not yet authorised via OAuth, provide user/password or access tokens" );
	#else
				Console.WriteLine ( "Not yet authorised via OAuth, provide user/password or access tokens" );
	#endif
				return;
			}
			
			// start connection thread
			ParameterizedThreadStart newThreadStart = new ParameterizedThreadStart( StartStream );
			connectingThread = new Thread( newThreadStart );
			connectingThread.Start ( postString.ToString () );
		}
		
		// disconnect streams if there are any
		public void Disconnect()
		{
			if ( connectionIsOpen )
			{
				currentResponse.Close ();
				currentReader.Close ();
				currentConnection.Abort();
			}
			
			if ( connectingThread != null )
				connectingThread.Abort ();	
			
			tweetParser.isParsingPaused = true;
		}
		
		private void StartStream( object postString )
		{		
			Console.WriteLine( "Starting connection to " + connectingRequest.RequestUri );
			// try to get stream
			HttpWebResponse newResponse = GetNewConnection ( (string)postString );
			Console.WriteLine( "Have response. Starting stream" );
			
			// disconnect old thread if there was one, streaming API only allows one connection per user
			if ( connectionIsOpen )
			{
				currentConnection.Abort();
				currentReader.Close ();
				currentResponse.Close ();
			}
			
			// save current connection
			connectionIsOpen = true;
			currentConnection = Thread.CurrentThread;
			currentResponse = newResponse;
			
			while ( true )
			{
				currentReader = new StreamReader( newResponse.GetResponseStream(), UTF8Encoding.UTF8 );
					
				// start reading
				ReadFromCurrentResponseStream ();
				currentResponse.Close ();
				
				// for streaming, restart when response ends
	#if FOR_UNITY
				Debug.Log ( "Response ended, trying to reconnect in " + ( networkErrors * 250 / 1000f ).ToString ( "F2" )  );
	#else
				Console.WriteLine ( "Response ended, trying to reconnect in " + ( networkErrors * 250 / 1000f ).ToString ( "F2" )  );
	#endif
				Thread.Sleep ( networkErrors * 250 );
				networkErrors++;
			
				currentResponse = GetNewConnection ( (string)postString );
			}		
		}
		
		// Read from response and add to queue
		private void ReadFromCurrentResponseStream()
		{
			if ( currentReader == null )
				return;
			
			tweetParser.isParsingPaused = false;
			
			// read new stream into tweet queue
			while ( !currentReader.EndOfStream )
			{
				string line = currentReader.ReadLine ();
				if ( line == "\n" )	// ignore keepalive statuses
					continue;
					
				tweetStringQueue.Enqueue( line );
	
				if ( tweetStringQueue.Count > maxInQueue )
					tweetStringQueue.Dequeue();
			}
		}
		
		// connect to API and get response object
		private HttpWebResponse GetNewConnection( string postString )
		{
			HttpWebResponse response = null;
			// keep trying until successful
			while ( response == null )
			{
				try 
				{
					// add parameters to webrequest
					byte[] postData = UTF8Encoding.UTF8.GetBytes ( (string)postString );
					connectingRequest.Method = "POST";
					connectingRequest.ContentLength = postData.Length;
					connectingRequest.ContentType = "application/x-www-form-urlencoded";
					Stream contentStream = connectingRequest.GetRequestStream();
					contentStream.Write ( postData, 0, postData.Length );
					contentStream.Close ();
					
					response = (HttpWebResponse)connectingRequest.GetResponse ();
				}
				catch ( WebException e )
				{
					int waitTime;
					if ( e.Status == WebExceptionStatus.ProtocolError )	// http level error
					{
						HttpWebResponse errorResponse = (HttpWebResponse)e.Response;
	#if FOR_UNITY
						Debug.Log (  errorResponse.StatusDescription + ". Trying again in " + ( httpRetryTime / 1000f ).ToString ( "F2" ) );
	#else
						Console.WriteLine (  errorResponse.StatusDescription + ". Trying again in " + ( httpRetryTime / 1000f ).ToString ( "F2" )  );
	#endif
						waitTime = httpRetryTime;
						httpRetryTime *= 2;
					}
					else	// network level error
					{
	#if FOR_UNITY
						Debug.Log (  e.Message + ".Trying again in " + ( networkErrors * 250 / 1000f ).ToString ( "F2" ) );
	#else
						Console.WriteLine ( e.Message + ".Trying again in " + ( networkErrors * 250 / 1000f ).ToString ( "F2" ) );
	#endif
						waitTime = networkErrors * 250;
						networkErrors++;
					}
					Thread.Sleep ( waitTime );
				}
			}
			
			return response;
		}
	}
}