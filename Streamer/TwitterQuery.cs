/* TwitterrQuery.cs - Eddie Cameron
 * ------
 * Base for all Twitter API query parameters
 * ------
 */

using System.Text;
using System.Collections.Generic;

namespace Streamer
{
	public abstract class TwitterQuery 
	{
		public abstract string GetKey();	// twitter URL name for this query
			
		public abstract string GetParameter();	// to return URL string with parameters
		
		public abstract void MergeQuery( TwitterQuery toMerge );	// combine toMerge parameters into this query
			
		public abstract void RemoveQuery( TwitterQuery toRemove );	// remove toRemove parameters from this query if they are there
	}
}