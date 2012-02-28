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

public class TwitterAccess
{
	private const string streamFilterURL = "https://stream.twitter.com/1/statuses/filter.json";
	private const string streamSampleURL = "https://stream.twitter.com/1/statuses/sample.json";
	
	private string consumerKey = "zFDzzVXJewaxd2ZwfagBg";					// register on dev.twitter.com to get
	private string consumerSecret = "XcIeaCsyXQyFvmF6SicBgrrDYjSHMCjGH2LLH7ecRI";	// these for your app
	private const string signatureMethod = "HMAC-SHA1";
	private const string version = "1.0";
	
	public bool isAuthed;
	private string token = "158618391-231q5UomSLpR3lVCLPOG3iqaKWW7vwtvUcdi69UU";	// generate an access token for yourself
	private string tokenSecret = "E8jsgHpYA3sustsBvHneHFu5B293IaaWtbS2fpvw0E";	// at dev.twitter.com
	
	// for the streaming API, you can use basic authentication...for now...
	private string username;
	private string password;
	
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
	private static string UrlEncode( string toEncode ) 
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
		
		isAuthed = token != "" && tokenSecret != "";
		
		tweetParser = new DecodeStream( tweetStringQueue );
		tweets = tweetParser.tweets;
		tweetParser.isParsingPaused = true;
	}
	
	
	// To add: user auth via OAuth
	
	
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
	
	
	// Connect with already given parameters
	public void Connect( bool useBasicAuth )
	{
		// if attempting connection, cancel
		if ( connectingThread != null )
			connectingThread.Abort ();
		
		// translate parameters to string values
		string[,] parameterStrings = new string[queries.Count,2];
		StringBuilder postString = new StringBuilder();
		
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
		
		// make and authorise webrequest
		connectingRequest = WebRequest.Create( streamFilterURL );
		
		if ( useBasicAuth )
			connectingRequest.Credentials = new NetworkCredential( username, password );
		else if ( isAuthed )
			connectingRequest.Headers.Add ( HttpRequestHeader.Authorization, MakeAuthorisation( parameterStrings ) );
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
	
	private string MakeAuthorisation( string[,] queryParameters )
	{
		// make nOnce
		StringBuilder no = new StringBuilder( 32 );
		string alphanumeric = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz1234567890";
		System.Random random = new System.Random();
		for ( int i = 0; i < 32; i++ )
			no.Append( alphanumeric[ random.Next ( alphanumeric.Length ) ] );
		
		// set timestamp
		string timeStamp = ( (int)( System.DateTime.UtcNow - new System.DateTime( 1970, 1, 1 ) ).TotalSeconds ).ToString();
		
		// sort all the parameters to be signed
		SortedDictionary<string, string> properties = new SortedDictionary<string, string>();
		for( int i = 0; i < queryParameters.GetLength( 0 ); i++ )
			properties.Add ( queryParameters[i,0], queryParameters[i,1] );
		
		properties.Add ( UrlEncode ( "oauth_consumer_key" ), UrlEncode( consumerKey ) );
		properties.Add ( UrlEncode( "oauth_nonce" ), no.ToString () );
		properties.Add ( UrlEncode( "oauth_signature_method" ), UrlEncode( signatureMethod ) );
		properties.Add ( UrlEncode( "oauth_timestamp" ), timeStamp );
		properties.Add ( "oauth_token", UrlEncode ( token ) );
		properties.Add ( "oauth_version", UrlEncode ( version ) );
		
		// make parameter string
		StringBuilder parameters = new StringBuilder();
		foreach( KeyValuePair<string, string> kv in properties )
			parameters.Append ( kv.Key + "=" +  kv.Value + "&" );
		parameters.Remove ( parameters.Length - 1, 1 );
		
		string postParameters =  "POST&" + UrlEncode ( streamFilterURL ) + "&" + UrlEncode ( parameters.ToString () );

		// sign parameter string
		byte[] sigBase = UTF8Encoding.UTF8.GetBytes (postParameters);
		MemoryStream ms = new MemoryStream();
		ms.Write (sigBase, 0, sigBase.Length );
		
		byte[] key = UTF8Encoding.UTF8.GetBytes ( UrlEncode ( consumerSecret ) + "&" + UrlEncode( tokenSecret ) );
		System.Security.Cryptography.HMACSHA1 sha1 = new System.Security.Cryptography.HMACSHA1( key );
		byte[] hashBytes = sha1.ComputeHash ( sigBase );
		string signature = System.Convert.ToBase64String( hashBytes );
		
		// construct auth header
		string header = "OAuth " + "oauth_consumer_key=\"" + UrlEncode ( consumerKey ) + "\",oauth_nonce=\"" + UrlEncode ( no.ToString() )
				+ "\",oauth_signature=\"" + UrlEncode ( signature ) + "\",oauth_signature_method=\"" + UrlEncode ( signatureMethod )
				+ "\",oauth_timestamp=\"" + UrlEncode ( timeStamp ) + "\",oauth_token=\"" + UrlEncode ( token )
				+ "\",oauth_version=\"" + UrlEncode ( version ) + "\"";
		
		return header;
	}
}
