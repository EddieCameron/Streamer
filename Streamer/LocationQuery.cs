/* QueryLocations.cs - Eddie Cameron
 * ------
 * Base for all queries to filter by location (Twitter location parameter)
 * ------
 */

using System;
using System.Text;
using System.Collections.Generic;

// location query - specified by bounding boxes to return results from
namespace Streamer
{
	public class QueryLocations : TwitterQuery
	{
		private List<Coordinates[]> locationsToQuery;
		
		public QueryLocations()
		{
			locationsToQuery = new List<Coordinates[]>();
		}
		
		public QueryLocations( Coordinates southwestCorner, Coordinates northeastCorner )
		{
			locationsToQuery = new List<Coordinates[]>();
			AddLocationBoundingBox( southwestCorner, northeastCorner );
		}
		
		public bool AddLocationBoundingBox( Coordinates southwestCorner, Coordinates northeastCorner )
		{	
			// Check is a valid box
			if ( northeastCorner.lng <= southwestCorner.lng )
				return false;
			
			if ( northeastCorner.lat <= southwestCorner.lat )
				return false;
			
			locationsToQuery.Add ( new Coordinates[]{ southwestCorner, northeastCorner } );
			return true;
		}
		
		public List<Coordinates[]> GetLocations()
		{
			return locationsToQuery;
		}
		
		public override string GetKey()
		{
			return "locations";
		}
		
		public override string GetParameter()
		{
			if ( locationsToQuery.Count == 0 )
				return "";
			
			StringBuilder sb = new StringBuilder();
			foreach( Coordinates[] locationBox in locationsToQuery )
			{
				foreach( Coordinates coord in locationBox )
					sb.Append ( coord.lng.ToString ( "F1" ) + "," + coord.lat.ToString( "F1" ) + "," );
			}
			sb.Remove ( sb.Length - 1, 1 );
			
			return sb.ToString ();
		}
		
		public override void MergeQuery (TwitterQuery toMerge)
		{
			if ( toMerge is QueryLocations )
			{
				locationsToQuery.AddRange( ((QueryLocations)toMerge ).GetLocations() );	
			}
		}
		
		public override void RemoveQuery (TwitterQuery toRemove)
		{
			if ( toRemove is QueryLocations )
			{
				foreach( Coordinates[] coord in ((QueryLocations)toRemove ).GetLocations() )
					locationsToQuery.Remove( coord );
			}
		}
	}
	
	public struct Coordinates
	{
		private float _lng;
		public float lng
		{
			get { return _lng; }
			set {
				if ( value < -180 )
					_lng = -180;
				else if ( value > 180 )
					_lng = 180f;
			}
		}
		
		private float _lat;
		public float lat
		{
			get { return _lat; }
			set {
				if ( value < -90 )
					_lat = -90;
				else if ( value > 90 )
					_lat = 90;
			}
		}
		
		public Coordinates( float lng, float lat )
		{
			this._lng = lng;
			this._lat = lat;
		}
	
	}
}
