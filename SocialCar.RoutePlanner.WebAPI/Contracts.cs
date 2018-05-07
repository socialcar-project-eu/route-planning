using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization;

namespace SocialCar.RoutePlanner.WebAPI.Contracts
{
    [DataContract]
    public class Trip
    {
        [DataMember(Name = "legs")]
        public List<Leg> Legs = new List<Leg>();

        [DataMember(Name = "weights")]
        public Routing.Router.RouterFactors RouterFactors;

        [DataMember(Name = "count_pt")]
        public int CountPT = 0;

        [DataMember(Name = "trip_duration")]
        public double TripDuration = 0;

        [DataMember(Name = "info")]
        public string Info = "";

        public Trip() { }

        public Trip(Trip Trip)
        {
            foreach (Leg leg in Trip.Legs)
                this.Legs.Add(leg);
            this.RouterFactors = new Routing.Router.RouterFactors(
                Trip.RouterFactors.WalkingFactor,
                Trip.RouterFactors.TransportationFactor,
                Trip.RouterFactors.TransportationChangeFactor,
                Trip.RouterFactors.CarpoolingFactor,
                Trip.RouterFactors.TrafficPropagationMaxDistance, 
                Trip.RouterFactors.Index, 
                Trip.RouterFactors.SecondsForward);
            this.CountPT = Trip.CountPT;
            this.TripDuration = Trip.TripDuration;
            this.Info = Trip.Info;
        }
    }

    [DataContract]
    public class Leg
    {
        [DataMember(Name = "transport")]
        public Transport Transport;

        [DataMember(Name = "route")]
        public Route Route;

        [DataMember(Name = "distance")]
        public string Distance;

        [DataMember(Name = "stops")]
        public string Stops;

        [DataMember(Name = "duration")]
        public string Duration;

        [DataMember(Name = "waiting_time")]
        public string WaitingTime;

        [DataMember(Name = "departure_time")]
        public string DepartureTime;
    }

    [DataContract]
    public class Transport
    {
        [DataMember(Name = "travel_mode")]
        public string TravelMode;

        [DataMember(Name = "long_name")]
        public string LongName;

        [DataMember(Name = "short_name")]
        public string ShortName;

        [DataMember(Name = "ride_id")]
        public string RideID;

        [DataMember(Name = "ride_name")]
        public string RideName;

        [DataMember(Name = "route_url")]
        public string RouteUrl;

        [DataMember(Name = "agency_id")]
        public string AgencyID;

        [DataMember(Name = "route_id")]
        public string RouteID;

        [DataMember(Name = "trip_id")]
        public string TripId;
    }

    [DataContract]
    public class Route
    {
        [DataMember (Name = "points")]
        public List<Point> Points = new List<Point>();
    }

    [DataContract]
    public class Point
    {
        [DataMember(Name = "address")]
        public string Address;

        [DataMember(Name = "point")]
        public Coordinates Coordinates;

        [DataMember(Name = "date")]
        public string Date;

        [DataMember(Name = "stop_id")]
        public string StopId;

        [DataMember(Name = "departure_time")]
        public string DepartureTime;
    }

    [DataContract]
    public class Coordinates
    {
        [DataMember(Name = "lat")]
        public string Latitude;

        [DataMember(Name = "lon")]
        public string Longitude;
    }
}
