/* QueryTrack.cs - Eddie Cameron
 * ------
 * Base for all queries to filter by strings (Twitter track parameter)
 * ------
 */

using System.Text;
using System.Collections.Generic;

namespace Streamer
{
	public class QueryTrack : TwitterQuery 
	{
		private List<string> stringsToTrack;
		
		public QueryTrack()
		{
			stringsToTrack = new List<string>();
		}
		
		public QueryTrack( string track )
		{
			stringsToTrack = new List<string>();
			stringsToTrack.Add ( track );
		}
	
		public List<string> GetTrackStrings()
		{
			return stringsToTrack;
		}
		
		public bool AddTrack( string track )
		{
			if ( track.ToCharArray().Length > 60 )
				return false;
			else
				stringsToTrack.Add( track );
			
			return true;
		}
		
		public override string GetKey()
		{
			return "track";
		}
			
		public override string GetParameter()
		{
			StringBuilder sb = new StringBuilder();
			foreach( string s in stringsToTrack )
				sb.Append( s + "," );
			sb.Remove( sb.Length - 1, 1 );
			return sb.ToString();
		}
		
		public override void MergeQuery( TwitterQuery toMerge )
		{
			if ( toMerge is QueryTrack )
				stringsToTrack.AddRange( ( (QueryTrack)toMerge ).GetTrackStrings() );
		}
			
		public override void RemoveQuery( TwitterQuery toRemove )
		{
			if ( toRemove is QueryTrack )
			{
				foreach( string t in ((QueryTrack)toRemove ).GetTrackStrings() )
					stringsToTrack.Remove( t );
			}
		}
	}
}
