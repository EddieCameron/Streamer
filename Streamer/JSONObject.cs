#define FROMTWITTER

/* JSONObject.cs - Eddie Cameron
 * ------
 * A rather simple JSON string to object parser
 * ------
 * Based on JSONObject, Copyright Matt Schoen 2010, from http://www.unifycommunity.com/wiki/index.php?title=Json
 * http://www.opensource.org/licenses/lgpl-2.1.php
 * ------
 */

using System.Collections.Generic;
using System.IO;
using System;

namespace Streamer
{
	public class JSONObject
	{
		const int MAX_DEPTH = 1000;
	    const string INFINITY = "\"INFINITY\"";
	    const string NEGINFINITY = "\"NEGINFINITY\"";
		public enum Type { NULL, STRING, NUMBER, OBJECT, ARRAY, BOOL }
		
		public Type type;
		public bool b;
		public double n;
		public string str;
		public List<string> keys; 
		public List<JSONObject> props;
			
		public bool HasProperty( string prop )
		{
			return keys.Contains ( prop );
		}
		
		public JSONObject GetProperty( string prop )
		{
			if ( type == Type.OBJECT || type == Type.ARRAY )
			{
				int propInd = keys.IndexOf ( prop );
				if ( propInd >= 0 && propInd < props.Count )
					return props[ propInd ];
			}
			
			return null;
		}
		
		public JSONObject( string str )
		{
			if ( str == "" )
			{
				type = Type.NULL;
				return;
			}
					
			if( str.Length > 0) 
			{
	        	if ( string.Compare(str, "true", true) == 0) 
				{
	                type = Type.BOOL;
	                b = true;
	            } 
				else if(string.Compare(str, "false", true) == 0) 
				{
	                type = Type.BOOL;
	                b = false;
	            } 
				else if(str == "null") 
				{
	                type = Type.NULL;
	            } 
				else if(str == INFINITY)
				{
	                type = Type.NUMBER;
	                n = double.PositiveInfinity;
	            } 
				else if(str == NEGINFINITY)
				{
	                type = Type.NUMBER;
	                n = double.NegativeInfinity;
	            } 
				else if(str[0] == '"') 
				{
	                type = Type.STRING;
					
					// unescape backslashes and unicode
					System.Text.StringBuilder sb = new System.Text.StringBuilder();
					for ( int i = 1; i < str.Length - 1; i++ )
					{
						char thisChar = str[i];
						if ( thisChar == '\\' )
						{
							// backslash
							i++;
							if ( i < str.Length - 5 && str[i] == 'u' )
							{
								// unicode
								byte[] uniBytes = new byte[2];
								uniBytes[1] = Convert.ToByte ( str[++i].ToString() + str[++i], 16 );
								uniBytes[0] = Convert.ToByte ( str[++i].ToString () + str[++i], 16 );
								sb.Append ( System.Text.UTF8Encoding.UTF8.GetChars ( uniBytes ) );
							}
							else if ( i < str.Length - 1 )
								sb.Append ( str[i] );
							else
								sb.Append ( '\\' );
						}
						else   // plain char
							sb.Append( thisChar );
					}
					
	                this.str = sb.ToString ();
	            } 
				else 
				{
	               	try 
					{
						// check for number type
	               		n = System.Convert.ToDouble(str);
	               		type = Type.NUMBER;
	               	} 
					catch(System.FormatException) 
					{
		            	int token_tmp = 0;
		                /*
		                 * Checking for the following formatting (www.json.org)
		                 * object - {"field1":value,"field2":value}
		                 * array - [value,value,value]
		                 * value - string   - "string"
		                 *     - number - 0.0
		                 *     - bool      - true -or- false
		                 *     - null      - null
		                 */
		                switch(str[0]) 
						{
			                case '{':
			                    type = Type.OBJECT;
			                   	keys = new List<string>();
								props = new List<JSONObject>();
			                    break;
			                case '[':
			                    type = Type.ARRAY;
								props = new List<JSONObject>();
			                    break;
			                default:
			                    type = Type.NULL;
			                    return;
	                    }
						
	                    int depth = 0;
	                    bool openquote = false;
	                    bool inProp = false;
	                    for ( int i = 1; i < str.Length; i++ ) 
						{
	#if !FROMTWITTER
							// Twitter feeds may have these characters in their statuses
	                        if(str[i] == '\\' || str[i] == '\t' || str[i] == '\n' || str[i] == '\r') {
	                            i++;
	                            continue;
							}
	#else
							if ( str[i] == '\\' )
							{
								if ( str.Length >= i & str[i+1] == '\"' )
								{	
									i++;
									continue;
								}
							}
	#endif
							
	                     	if(str[i] == '"')
	                            openquote = !openquote;
	                        else if( str[i] == '[' || str[i] == '{' )
	                            depth++;
							
	                        if(depth == 0 && !openquote) 
							{
	                            if( str[i] == ':' && !inProp ) 
								{
	                                inProp = true;
	                                try {
	                                    keys.Add( str.Substring( token_tmp + 2, i - token_tmp - 3));
	                                } catch {  }
	                                token_tmp = i;
	                            }
	                            if( str[i] == ',' ) 
								{
	                                inProp = false;
	                                props.Add(new JSONObject(str.Substring(token_tmp + 1, i - token_tmp - 1)));
	                                token_tmp = i;
	                            }
	                            if(str[i] == ']' || str[i] == '}')
								{
									if ( str[i - 1] != '[' && str[i-1] != '{' )
	                               		props.Add(new JSONObject(str.Substring(token_tmp + 1, i - token_tmp - 1)));
								}
	                        }
							
	                        if(str[i] == ']' || str[i] == '}')
	                            depth--;
	                    }
	                }
	            }
	        }
		}
	}
}