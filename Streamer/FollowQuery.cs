/* QueryTrack.cs - Eddie Cameron
 * ------
 * Base for all queries to filter by strings (Twitter track parameter)
 * ------
 */

using System.Text;
using System.Collections.Generic;

namespace Streamer
{
	public class QueryFollow : TwitterQuery 
	{
		private List<int> idsToFollow;
		
		public QueryFollow()
		{
			idsToFollow = new List<int>();
		}
		
		public QueryFollow( int id )
		{
			idsToFollow = new List<int>();
			idsToFollow.Add ( id );
		}
	
		public List<int> GetFollowedIDs()
		{
			return idsToFollow;
		}
		
		public bool AddID( int id )
		{
			idsToFollow.Add( id );
			
			return true;
		}
		
		public override string GetKey()
		{
			return "follow";
		}
			
		public override string GetParameter()
		{
			StringBuilder sb = new StringBuilder();
			foreach( int id in idsToFollow )
				sb.Append( id.ToString () + "," );
			sb.Remove( sb.Length - 1, 1 );
			return sb.ToString();
		}
		
		public override void MergeQuery( TwitterQuery toMerge )
		{
			QueryFollow followMerge = toMerge as QueryFollow;
			if ( followMerge != null )
				idsToFollow.AddRange( followMerge.GetFollowedIDs() );
		}
			
		public override void RemoveQuery( TwitterQuery toRemove )
		{
			QueryFollow followMerge = toRemove  as QueryFollow;
			if ( followMerge != null )
			{
				foreach( int id in followMerge.GetFollowedIDs() )
					idsToFollow.Remove( id );
			}
		}
	}
}
