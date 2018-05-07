using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocialCar.RoutePlanner.Routing
{
    [Serializable]
    public enum RouteType
    {
        Tram = 0,
        Metro = 1,
        Rail = 2,
        Bus = 3,
        Ferry = 4,
        CableCar = 5,
        Gondola = 6,
        Funicular = 7
    }

    [Serializable]
    public class TimeTableEntry
    {
        public int SourceArrivalTime { get; private set; }
        public int SourceDepartureTime { get; private set; }
        public int DestinationArrivalTime { get; private set; }
        public int DestinationDepartureTime { get; private set; }

        public string RouteId { get; private set; }
        public string RouteLongName { get; private set; }
        public string RouteShortName { get; private set; }
        public string RouteDesc { get; private set; }
        public string RouteUrl { get; private set; }
        public string TripId { get; private set; }
        public string ServiceID { get; private set; }

        public RouteType RouteType { get; private set; }

        public List<string> ServiceDates { get; private set; }

        public string AgencyId { get; private set; }

        public TimeTableEntry(int SrcArrivalTime, int SrcDepartureTime, int DstArrivalTime, int DstDepartureTime,
            string routeId, string routeLongName, string routeShortName, string routeDesc, string routeType, string routeUrl,
            string tripId, string serviceId, List<string> Dates, string agencyId)
        {
            this.SourceArrivalTime = SrcArrivalTime;
            this.SourceDepartureTime = SrcDepartureTime;
            this.DestinationArrivalTime = DstArrivalTime;
            this.DestinationDepartureTime = DstDepartureTime;
            this.RouteId = routeId;
            this.TripId = tripId;
            this.RouteLongName = routeLongName;
            this.RouteShortName = routeShortName;
            this.RouteDesc = routeDesc;
            this.ServiceID = serviceId;

            this.RouteType = GetRouteType(routeType);
            this.RouteUrl = routeUrl;

            this.ServiceDates = new List<string>();
            this.ServiceDates.AddRange(Dates);

            this.AgencyId = agencyId;
        }

        public bool IsValidDate(string date)
        {
            return ServiceDates.Contains(date);
        }

        private RouteType GetRouteType(string Type)
        {
            return (RouteType)int.Parse(Type);
        }
    }

    [Serializable]
    public class TimeTable
    {
        public readonly List<TimeTableEntry> Entries;
        public string RouteId { get; private set; }
        public RouteType RouteType { get; private set; }

        public TimeTable()
        {
            this.Entries = new List<TimeTableEntry>();
        }

        public void AddEntry(TimeTableEntry Entry)
        {
            Entries.Add(Entry);
            RouteId = Entries.First().RouteId;
            RouteType = Entry.RouteType;
        }

        public List<TimeTableEntry> GetFeasibleTrips(double Time)
        {
            return this.Entries.Where(x => x.SourceDepartureTime >= Time)
                .OrderBy(x => x.SourceDepartureTime).ToList();
        }

        public int TripCount()
        {
            return Entries.Count;
        }
    }
}
