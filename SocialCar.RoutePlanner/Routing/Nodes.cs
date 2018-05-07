using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Device.Location;

using SocialCar.RoutePlanner.Routing.Connections;

namespace SocialCar.RoutePlanner.Routing.Nodes
{
    [Serializable]
    public class Point
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public Point(double lat, double lng)
        {
            Latitude = lat;
            Longitude = lng;
        }

        public override string ToString()
        {
            return Latitude + ", " + Longitude;
        }

        public double DistanceFrom(Point P)
        {
            return new GeoCoordinate(Latitude, Longitude).GetDistanceTo(new GeoCoordinate(P.Latitude, P.Longitude));
        }
    }

    [Serializable]
    public abstract class Node
    {
        public long Id { get; protected set; }
        public Point Point { get; set; }

        public List<Connections.Connection> Connections { get; protected set; }

        public Node(long id, Point Coordinates)
        {
            this.Id = id;
            this.Point = Coordinates;
            this.Connections = new List<Connection>();
        }

        //public List<Connection> GetConnections()
        //{
        //    List<Connection> cns = new List<Connection>();
        //    cns.AddRange(this.Connections);

        //    return cns;
        //}
    }

    [Serializable]
    public class RNode : Node
    {
        public RNode(long id, Point Coordinates)
            : base(id, Coordinates)
        {

        }
    }

    [Serializable]
    public class TNode : RNode
    {
        public string StopId { get; set; }
        public string StopCode { get; set; }
        public string StopName { get; set; }
        public string ZoneId { get; set; }

        public TNode(long id, Point Coordinates)
            : base(id, Coordinates)
        {
            
        }
    }

    [Serializable]
    public class TNodeCarpooling : TNode
    {
        public double distanceFromStartnode;
        public long keyTmp;

        public TNodeCarpooling(long id, Point Coordinates, string stopId, string stopCode, string stopName, double distance, long keyTmp = 0)
        : base(id, Coordinates)
        {
            this.distanceFromStartnode = distance;
            this.StopId = stopId;
            this.StopCode = stopCode;
            this.StopName = stopName;
            this.keyTmp = keyTmp;
        }
    }

    [Serializable]
    public class CNode : RNode
    {
        public string StopName;

        public CNode(long id, Point Coordinates, string stopName="")
            : base(id, Coordinates)
        {
            this.StopName = stopName;
        }
    }

    
}
