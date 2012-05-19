/*
 * OAuth.cs - Eddie Cameron
 * -------
 * Sets up OAuth for Streamer.
 * 
 * Consumer key & secret must be hard coded. Get your Twitter API codes by registering your app at api.twitter.com
 * 
 * Process to authenticate your user:
 * 
 * Call StartOAuthRequest, which will query the given URL for a token/secret to sign a request with, and an URL for the user
 * to go to to authorise the app.
 * 
 * Redirect user to the given URL, they will be given a passcode.
 * 
 * Get the user to enter the passcode into your app, and provide to GetAccessTokens. If passcode is valid, a token/secret pair 
 * will be sent back to use for any requests. Authorisation is complete!
 * 
 * (For Twitter auth, use the equivilent functions in TwitterAccess.cs, which will provide the URLs for you)
 * -------
 */


#if FOR_UNITY
using UnityEngine;
#endif
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Net;
using System.Threading;
using System;

namespace Streamer
{
	public class OAuth 
	{		
		/* -~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~*/
		private readonly string consumerKey = "";		// register on dev.twitter.com to get
		private readonly string consumerSecret = "";	// these for your app
		/* -~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~*/
		
		private readonly string signatureMethod = "HMAC-SHA1";
		private readonly string version = "1.0";
		
		private string token = "";			// to be set by following token request process
		private string tokenSecret = "";	//
		
		struct AuthRequestInfo{
			public HttpWebRequest request;
			public Action<string> callback;
			public string redirectUrl;
		}
				
		OAuthStatus _status = OAuthStatus.Unauthorised;
		public OAuthStatus status { get{ return _status; } }
		
		/// <summary>
		/// Begins the OAuth authorisation. Asks given URL for request token/secret
		/// Will return an auth URl to the callback method.
		/// User (or app) should open this URL and after authorising this app, the user will get a passcode.
		/// 
		/// The user should be prompted to enter this passcode, which is passed to GetUserTokens to finish authorisation
		/// </summary>
		/// <param name='requestUrl'>
		/// requestUrl. : Url to ask for request token/secret from 
		/// </param>
		/// <param name='userRedirectUrl'>
		/// userRedirectUrl. :  Url where user should be directed to for an access token (parameters added by this method)
		/// </param>
		public void StartOAuthRequest( string requestUrl, string userRedirectUrl, System.Action<string> receiveUrlCallback )
		{
			// only allow one request at a time
			if ( status == OAuthStatus.RequestingTokenURL || status == OAuthStatus.RequestingUserTokens || status == OAuthStatus.HasRequestTokenUrl )
				return;
			_status = OAuthStatus.RequestingTokenURL;
				
			token = "";
			tokenSecret = "";
			
			string[,] callbackParameter = new string[,]{{"oauth_callback","oob"}};
			
			WebRequest request = WebRequest.Create ( requestUrl );
			request.Method = "POST";
			request.ContentLength = 0;
			request.ContentType = "application/x-www-form-urlencoded";
			request.Headers.Add ( HttpRequestHeader.Authorization, AuthoriseRequest ( callbackParameter, requestUrl, false ) );
			
			AuthRequestInfo info;
			info.request = (HttpWebRequest)request;
			info.callback = receiveUrlCallback;
			info.redirectUrl = userRedirectUrl;
			
			request.BeginGetResponse ( new AsyncCallback( OnRequestResponse ), info );
		}
		
		// Processes response to token request
		void OnRequestResponse( IAsyncResult result )
		{
			if ( status != OAuthStatus.RequestingTokenURL )
				return;
			
			bool successfulRequest = false;
			HttpWebRequest request = null;
			HttpWebResponse response = null;
			AuthRequestInfo info = (AuthRequestInfo)result.AsyncState;
			try
			{
				request = info.request;
				response = (HttpWebResponse)request.EndGetResponse ( result );
							
				if ( response.StatusCode == HttpStatusCode.OK )
				{
					using( StreamReader reader = new StreamReader( response.GetResponseStream (), UTF8Encoding.UTF8 ) )
					{
						string[] reply = reader.ReadLine ().Split ( "&".ToCharArray() );
						if ( reply.Length >= 3 )
						{
							token = reply[0].TrimStart ( "oauth_token=".ToCharArray() );
							tokenSecret = reply[1].TrimStart ( "oauth_token_secret=".ToCharArray () );
							if ( reply[2].EndsWith ( "true" ) )
								successfulRequest = true;
						}
					}
				}
				else
					throw new WebException( response.StatusDescription );
			}
			catch ( Exception e )
			{
#if FOR_UNITY
				Debug.LogWarning ( e.Message );
#else
				Console.WriteLine ( e.Message );
#endif
			}
			finally
			{
				request.Abort ();
				response.Close();
			}
			
			if ( successfulRequest )
			{
				_status = OAuthStatus.HasRequestTokenUrl;
			
				if( info.callback != null )
					info.callback ( info.redirectUrl + "?oauth_token=" + token );
			}
			else
			{
				token = "";
				tokenSecret = "";
				_status = OAuthStatus.Unauthorised;
			}
		}
		
		/// <summary>
		/// Swaps a passcode and request token/secret for auth token/secret pair to be used for all oauthed requests
		/// </summary>
		/// <param name='url'>
		/// URL. : Url from which access 
		/// </param>
		/// <param name='passcode'>
		/// Passcode.
		/// </param>
		public void GetAccessTokens( string url, string passcode )
		{
			// only can be called after just receiving valid request token
			if ( _status != OAuthStatus.HasRequestTokenUrl )
				return;
			_status = OAuthStatus.RequestingUserTokens;
			
			string[,] verifier = new string[,]{{"oauth_verifier", passcode}};
			
			WebRequest request = WebRequest.Create ( url );
			request.Headers.Add ( HttpRequestHeader.Authorization, AuthoriseRequest ( verifier, url ) );
			
			// add parameters to webrequest
			byte[] postData = UTF8Encoding.UTF8.GetBytes ( "oauth_verifier=" + passcode );
			request.Method = "POST";
			request.ContentLength = postData.Length;
			request.ContentType = "application/x-www-form-urlencoded";
			Stream contentStream = request.GetRequestStream();
			contentStream.Write ( postData, 0, postData.Length );
			contentStream.Close ();
			
			request.BeginGetResponse ( new AsyncCallback( OnReceiveAccessTokens ), request );
		}
		
		void OnReceiveAccessTokens( IAsyncResult result )
		{
			if ( status != OAuthStatus.RequestingUserTokens )
				return;
			
			bool successfulRequest = false;
			HttpWebRequest request = null;
			HttpWebResponse response = null;
			try
			{
				request = (HttpWebRequest)result.AsyncState;
				response = (HttpWebResponse)request.EndGetResponse ( result );
							
				if ( response.StatusCode == HttpStatusCode.OK )
				{
					using( StreamReader reader = new StreamReader( response.GetResponseStream (), UTF8Encoding.UTF8 ) )
					{
						string[] reply = reader.ReadLine ().Split ( "&".ToCharArray() );
						if ( reply.Length >= 2 )
						{
							token = reply[0].TrimStart ( "oauth_token=".ToCharArray() );
							tokenSecret = reply[1].TrimStart ( "oauth_token_secret=".ToCharArray () );
							successfulRequest = true;
						}
					}
				}
				else
					throw new WebException( response.StatusDescription );
			}
			catch ( Exception e )
			{
#if FOR_UNITY
				Debug.LogWarning ( e.Message );
#else
				Console.WriteLine ( e.Message );
#endif
			}
			
			request.Abort ();
			response.Close();
			
			if ( successfulRequest )
				_status = OAuthStatus.Authorised;
			else
			{
				token = "";
				tokenSecret = "";
				_status = OAuthStatus.Unauthorised;
			}		
		}
		
		public string AuthoriseRequest( string[,] queryParameters, string url, bool useToken = true )
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
			
			properties.Add ( TwitterAccess.UrlEncode ( "oauth_consumer_key" ), TwitterAccess.UrlEncode( consumerKey ) );
			properties.Add ( TwitterAccess.UrlEncode( "oauth_nonce" ), no.ToString () );
			properties.Add ( TwitterAccess.UrlEncode( "oauth_signature_method" ), TwitterAccess.UrlEncode( signatureMethod ) );
			properties.Add ( TwitterAccess.UrlEncode( "oauth_timestamp" ), timeStamp );
			if ( useToken )
				properties.Add ( "oauth_token", TwitterAccess.UrlEncode ( token ) );
			properties.Add ( "oauth_version", TwitterAccess.UrlEncode ( version ) );
			
			// make parameter string
			StringBuilder parameters = new StringBuilder();
			foreach( KeyValuePair<string, string> kv in properties )
				parameters.Append ( kv.Key + "=" +  kv.Value + "&" );
			parameters.Remove ( parameters.Length - 1, 1 );
			
			// generate signature
			string postParameters =  "POST&" + TwitterAccess.UrlEncode ( url ) + "&" + TwitterAccess.UrlEncode ( parameters.ToString () );
			string signature;
			if ( useToken )
				signature = SignString ( postParameters, TwitterAccess.UrlEncode ( consumerSecret ) + "&" + TwitterAccess.UrlEncode( tokenSecret ) );
			else
				signature = SignString ( postParameters, TwitterAccess.UrlEncode ( consumerSecret ) + "&" );
			properties.Add ( "oauth_signature", TwitterAccess.UrlEncode ( signature ) );
			
			// construct auth header
			// oauth_verifier is special case used only when getting new access tokens
			StringBuilder header = new StringBuilder( "OAuth " );
			foreach( KeyValuePair<string, string> parameter in properties )
			{ 
				if ( parameter.Key.StartsWith ( "oauth" ) && parameter.Key != "oauth_verifier" )
				{
					header.Append( parameter.Key );
					header.Append( "=\"" );
					header.Append ( parameter.Value );
					header.Append ( "\"," );
				}
			}
			header.Remove ( header.Length - 1, 1 );
			
			return header.ToString();
		}
		
		string SignString( string toSign, string withKey )
		{
			// sign parameter string
			byte[] sigBase = UTF8Encoding.UTF8.GetBytes ( toSign );
			MemoryStream ms = new MemoryStream();
			ms.Write (sigBase, 0, sigBase.Length );
			
			byte[] key = UTF8Encoding.UTF8.GetBytes ( withKey );
			System.Security.Cryptography.HMACSHA1 sha1 = new System.Security.Cryptography.HMACSHA1( key );
			byte[] hashBytes = sha1.ComputeHash ( sigBase );
			return System.Convert.ToBase64String( hashBytes );
		}
		
		/// <summary>
		/// Gets a string representing the user's auth info to be saved.
		/// TODO : Obfuscate this
		/// </summary>
		/// <returns>
		/// The user auth.
		/// </returns>
		public string GetUserAuth()
		{
			if ( status == OAuthStatus.Authorised )
				return token + "&" + tokenSecret;
			else
				return "";
		}
		
		public bool AuthoriseFromSave( string savedAuthInfo )
		{
			string[] splitInfo = savedAuthInfo.Split ( "&".ToCharArray() );
			if ( splitInfo.Length != 2 )
				return false;
			
			token = splitInfo[0];
			tokenSecret = splitInfo[1];
			
			_status = OAuthStatus.Authorised;
			return true;
		}
	}
}

public enum OAuthStatus
{
	Unauthorised,
	RequestingTokenURL,
	HasRequestTokenUrl,
	RequestingUserTokens,
	Authorised
}