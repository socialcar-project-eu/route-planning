using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SocialCar.RoutePlanner.Traffic;

using SocialCar.RoutePlanner.Routing.Nodes;
using System.Configuration;
using SocialCar.RoutePlanner.Carpools;

namespace SocialCar.RoutePlanner.Routing.Connections
{
    [Serializable]
    public abstract class Connection
    {
        public long Id { get; protected set; }
        public Dictionary<string, string> Tags { get; protected set; }
        public static Connection INVALID
        {
            get
            {
                return null;
            }
        }

        //public TripHistoryDetails TripHistory = new TripHistoryDetails();

        public Connection(long id)
        {
            this.Id = id;
            Tags = new Dictionary<string, string>();
        }

        public void AddTag(string key, string value)
        {
            this.Tags.Add(key, value);
        }

        public void CopyTags(Dictionary<string, string> T)
        {
            foreach (KeyValuePair<string, string> kvp in T)
            {
                Tags.Add(kvp.Key, kvp.Value);
            }
        }

        public string GetTagValue(string key)
        {
            if (Tags.ContainsKey(key))
            {
                return Tags[key];
            }
            return null;
        }

        public void ChangeTagValue(string key, string value)
        {
            if (Tags.ContainsKey(key))
            {
                Tags[key] = value;
            }
            else
            {
                Tags.Add(key, value);
            }
        }

        public double addTrafficDelay(double time, double distanceFromTraffic, TrafficReport TrafficReport)
        {
            double trafficDelay = 0;
            
            switch (TrafficReport.Severity)
            {
                case TrafficSeverity.Low:
                    if ((distanceFromTraffic <= TrafficReport.TrafficPropagationMaxDistance) && (distanceFromTraffic >= (TrafficReport.TrafficPropagationMaxDistance / 2)))
                        trafficDelay = time * 0.5;
                    break;

                case TrafficSeverity.Medium:
                    if ((distanceFromTraffic <= TrafficReport.TrafficPropagationMaxDistance) && (distanceFromTraffic >= (TrafficReport.TrafficPropagationMaxDistance / 2)))
                        trafficDelay = time * 1;
                    else if ((distanceFromTraffic <= (TrafficReport.TrafficPropagationMaxDistance / 2)) && (distanceFromTraffic >= (TrafficReport.TrafficPropagationMaxDistance / 3)))
                        trafficDelay = time * 0.5;
                    break;

                case TrafficSeverity.High:
                    if ((distanceFromTraffic <= TrafficReport.TrafficPropagationMaxDistance) && (distanceFromTraffic >= (TrafficReport.TrafficPropagationMaxDistance / 2)) )
                        trafficDelay = time * 2;
                    else if ((distanceFromTraffic <= (TrafficReport.TrafficPropagationMaxDistance / 2)) && (distanceFromTraffic >= (TrafficReport.TrafficPropagationMaxDistance / 3)))
                        trafficDelay = time * 1;
                    else if ((distanceFromTraffic <= (TrafficReport.TrafficPropagationMaxDistance / 3)) && (distanceFromTraffic >= (TrafficReport.TrafficPropagationMaxDistance / 4)))
                        trafficDelay = time * 0.5;
                    break;

                default:
                    break;
            }

            return trafficDelay;
        }

        public abstract double GetDistance(TravelMode Mode);

        public abstract Node GetSource();
        public abstract Node GetDestination();

        public abstract double GetWaitingTime();
        public abstract int GetDepartureTime();
        public abstract string GetRouteId();

        public abstract bool CanBeTraversedUsingMode(TravelMode Mode);

        //public abstract double GetTravelTime(TravelMode Mode);
        public abstract double GetTravelTime(string date, double arrivalTime, TravelMode Mode);

        public abstract string GetRouteLongName();
        public abstract string GetRouteShortName();
        public abstract string GetServiceId();

        public abstract TimeTableEntry GetTimeTableEntry();
        public abstract TimeTable GetTimeTable();

        /* Carpooling functions */
        public abstract string GetCarpoolerId();

        /* Traffic functions */
        public abstract void setTrafficReport(TrafficReport TrafficReport);
        public abstract TrafficReport getTrafficReport();
        public abstract void setTrafficDistanceFromDestination(double trafficDistance);
        public abstract double getTrafficDistanceFromDestination();
        public abstract void setTrafficDistanceFromSource(double trafficDistance);
        public abstract double getTrafficDistanceFromSource();

        public abstract bool GetOneWay();
        public abstract bool GetOnlyFoot();
    }

    [Serializable]
    public class RConnection : Connection
    {
        public RNode Source { get; private set; }
        public RNode Destination { get; private set; }
        public double Distance { get; private set; }
        public double WaitingTime { get; private set; }
        public int DepartureTime { get; private set; }

        public readonly bool OneWay;
        public readonly bool OnlyFoot;

        public TrafficReport TrafficReport { get; private set; }
        public double TrafficDistanceFromDestination { get; private set; }
        public double TrafficDistanceFromSource { get; private set; }

        public RConnection(long id, RNode src, RNode dst, bool isOneWay, bool isOnlyFoot)
            : base(id)
        {
            this.Source = src;
            this.Destination = dst;
            this.Distance = Source.Point.DistanceFrom(Destination.Point);
            this.OneWay = isOneWay;
            this.OnlyFoot = isOnlyFoot;
        }

        public override double GetDistance(TravelMode Mode)
        {
            if (Mode == TravelMode.Car)
            {
                if (!CanBeTraversedUsingMode(Mode)) 
                    return double.PositiveInfinity;
                if (!ValidateTraversal(Mode)) 
                    return double.PositiveInfinity;
            }

            return this.Distance;
        }

        public override bool GetOneWay()
        {
            return this.OneWay;
        }

        public override bool GetOnlyFoot()
        {
            return this.OneWay;
        }

        public override Node GetDestination()
        {
            return this.Destination;
        }

        public override Node GetSource()
        {
            return this.Source;
        }

        public override double GetWaitingTime()
        {
            return this.WaitingTime;
        }

        public override int GetDepartureTime()
        {
            return this.DepartureTime;
        }

        public override void setTrafficReport(TrafficReport TrafficReport)
        {
            this.TrafficReport = TrafficReport;
        }

        public override TrafficReport getTrafficReport()
        {
            return this.TrafficReport;
        }

        public override void setTrafficDistanceFromDestination(double trafficDistance)
        {
            this.TrafficDistanceFromDestination = trafficDistance;
        }

        public override double getTrafficDistanceFromDestination()
        {
            return this.TrafficDistanceFromDestination;
        }

        public override void setTrafficDistanceFromSource(double trafficDistance)
        {
            this.TrafficDistanceFromSource = trafficDistance;
        }

        public override double getTrafficDistanceFromSource()
        {
            return this.TrafficDistanceFromSource;
        }

        public override string GetRouteId()
        {
            throw new NotImplementedException();
        }

        public override string GetRouteLongName()
        {
            throw new NotImplementedException();
        }

        public override string GetRouteShortName()
        {
            throw new NotImplementedException();
        }

        public override string GetServiceId()
        {
            throw new NotImplementedException();
        }

        public override TimeTableEntry GetTimeTableEntry()
        {
            throw new NotImplementedException();
        }

        public override TimeTable GetTimeTable()
        {
            throw new NotImplementedException();
        }

        public override string GetCarpoolerId()
        {
            throw new NotImplementedException();
        }

        /* RConnection */
        public override double GetTravelTime(string date, double arrivalTime, TravelMode Mode)
        {
            double time = 0;
            double trafficDelay = 0;

            if (Mode == TravelMode.Car)
            {
                if (!CanBeTraversedUsingMode(Mode)) 
                    return double.PositiveInfinity;
                if (!ValidateTraversal(Mode)) 
                    return double.PositiveInfinity;

                // Assuming that the average driving speed is 40 km/h
                int maxSpeed = 40;
                try
                {
                    if (Tags.ContainsKey("maxspeed"))
                    {
                        string maxSpeedStr = Tags["maxspeed"];
                        if (maxSpeedStr.Contains(";"))
                            maxSpeed = int.Parse(maxSpeedStr.Split(';')[0]);
                    }
                    else
                        maxSpeed = 40;
                }
                catch (Exception ex)
                {
                    maxSpeed = 40;
                }

                time = (Distance / 1000) / (float)(maxSpeed / 2);
                time = time * 3600;

                /* For Car, the time is traffic dependent */
                if (TrafficReport != null)
                {
                    double minDistanceFromTraffic;
                    minDistanceFromTraffic = Math.Min(this.TrafficDistanceFromSource, this.TrafficDistanceFromDestination);
                    trafficDelay = addTrafficDelay(time, minDistanceFromTraffic, TrafficReport);
                }

                time += trafficDelay;

                DepartureTime = (int)arrivalTime;
            }
            else if (Mode.HasFlag(Routing.TravelMode.Walking))
            {
                // The average walking speed is considered to be 1.4 m/s.
                if (!CanBeTraversedUsingMode(Mode)) return double.PositiveInfinity;
                time = Distance / (float)1.4;
                DepartureTime = (int)arrivalTime;
            }
            else
            {
                throw new Exception();
            }

            return time;
        }

        private bool ValidateTraversal(TravelMode Mode)
        {
            if ((Mode == TravelMode.Car) && OnlyFoot)
            {
                return false;
            }

            return true;
        }

        public override bool CanBeTraversedUsingMode(TravelMode Mode)
        {
            if (Mode.HasFlag(TravelMode.Walking) && !Mode.HasFlag(TravelMode.Car))
            {
                if (this.Tags.ContainsKey("foot"))
                {
                    string footValue = this.Tags["foot"];
                    if (footValue == "no")
                    {
                        return false;
                    }
                    else
                        return true;
                }

                //if (this.Tags.ContainsKey("highway"))
                //{
                //    string highwayValue = this.Tags["highway"];
                //    if (highwayValue == "primary" || highwayValue == "secondary" || highwayValue == "tertiary" ||
                //        highwayValue == "unclassified" || highwayValue == "residential" || highwayValue == "service" ||
                //        highwayValue == "living_street" || highwayValue == "pedestrian" || highwayValue == "track" ||
                //        highwayValue == "footway" || highwayValue == "bridleway" || highwayValue == "steps" ||
                //        highwayValue == "path" || highwayValue == "bridleway" || highwayValue == "crossing" ||
                //        highwayValue == "trunk_link")
                //    {
                //        return true;
                //    }
                //    else
                //        return false;
                //}
            }
            else if (!Mode.HasFlag(TravelMode.Walking) && (Mode.HasFlag(TravelMode.Car)))
            {
                if (this.Tags.ContainsKey("highway"))
                {
                    string highwayValue = this.Tags["highway"];
                    if (highwayValue == "motorway" || highwayValue == "trunk" || highwayValue == "primary" ||
                        highwayValue == "secondary" || highwayValue == "tertiary" || highwayValue == "unclassified" ||
                        highwayValue == "residential" || highwayValue == "motorway_link" || highwayValue == "trunk_link" ||
                        highwayValue == "primary_link" || highwayValue == "secondary_link" || highwayValue == "tertiary_link" ||
                        highwayValue == "living_street" || highwayValue == "road")
                    {
                        return true;
                    }
                    else
                        return false;
                }
            }

            return true;
        }
    }

    [Serializable]
    public class LConnection : Connection
    {
        public RNode Source { get; private set; }
        public RNode Destination { get; private set; }
        public double Distance { get; private set; }
        public double WaitingTime { get; private set; }
        public int DepartureTime { get; private set; }

        public LConnection(long id, RNode src, RNode dst)
            : base(id)
        {
            this.Source = src;
            this.Destination = dst;
            this.Distance = Source.Point.DistanceFrom(Destination.Point);
        }

        public override double GetDistance(TravelMode Mode)
        {
            return this.Distance;
        }

        public override Node GetSource()
        {
            return this.Source;
        }

        public override Node GetDestination()
        {
            return this.Destination;
        }

        public override double GetWaitingTime()
        {
            return this.WaitingTime;
        }

        public override int GetDepartureTime()
        {
            return this.DepartureTime;
        }

        public override bool GetOneWay()
        {
            throw new NotImplementedException();
        }

        public override bool GetOnlyFoot()
        {
            throw new NotImplementedException();
        }

        public override void setTrafficReport(TrafficReport TrafficReport)
        {
            throw new NotImplementedException();
        }

        public override TrafficReport getTrafficReport()
        {
            throw new NotImplementedException();
        }

        public override void setTrafficDistanceFromDestination(double trafficDistance)
        {
            throw new NotImplementedException();
        }

        public override double getTrafficDistanceFromDestination()
        {
            throw new NotImplementedException();
        }

        public override void setTrafficDistanceFromSource(double trafficDistance)
        {
            throw new NotImplementedException();
        }

        public override double getTrafficDistanceFromSource()
        {
            throw new NotImplementedException();
        }

        public override string GetRouteId()
        {
            throw new NotImplementedException();
        }

        public override string GetRouteLongName()
        {
            throw new NotImplementedException();
        }

        public override string GetRouteShortName()
        {
            throw new NotImplementedException();
        }

        public override string GetServiceId()
        {
            throw new NotImplementedException();
        }

        public override TimeTableEntry GetTimeTableEntry()
        {
            throw new NotImplementedException();
        }

        public override TimeTable GetTimeTable()
        {
            throw new NotImplementedException();
        }
        
        public override string GetCarpoolerId()
        {
            throw new NotImplementedException();
        }

        /* LConnection */
        public override double GetTravelTime(string date, double arrivalTime, TravelMode Mode)
        {
            double time = 0;

            if (Mode.HasFlag(Routing.TravelMode.Bus))
            {
                if (!CanBeTraversedUsingMode(Mode)) time = double.PositiveInfinity;
            }
            else
            {
                time = double.PositiveInfinity;
            }

            return time;
        }

        public override bool CanBeTraversedUsingMode(TravelMode Mode)
        {
            return Mode.HasFlag(TravelMode.Bus) || Mode.HasFlag(TravelMode.Carpool);
        }
    }

    [Serializable]
    public class TConnection : Connection
    {
        public TNode Source { get; private set; }
        public TNode Destination { get; private set; }
        public double Distance { get; private set; }
        public double WaitingTime { get; private set; }
        public int DepartureTime { get; private set; }
        public int ArrivalTime { get; private set; }
        public int DestArrivalTime { get; private set; }
        public int DestDepartureTime { get; private set; }

        public readonly TimeTable TimeTable;

        public TrafficReport TrafficReport { get; private set; }
        public double TrafficDistanceFromDestination { get; private set; }
        public double TrafficDistanceFromSource { get; private set; }

        public TConnection(long id, TNode src, TNode dst)
            : base(id)
        {
            this.Source = src;
            this.Destination = dst;
            this.Distance = Source.Point.DistanceFrom(Destination.Point);
            this.TimeTable = new TimeTable();
            this.DepartureTime = -1;
            this.ArrivalTime = -1;
            this.DestArrivalTime = -1;
        }

        public override double GetDistance(TravelMode Mode)
        {
            return this.Distance;
        }

        public override Node GetSource()
        {
            return this.Source;
        }

        public override Node GetDestination()
        {
            return this.Destination;
        }

        public override double GetWaitingTime()
        {
            return this.WaitingTime;
        }

        public override int GetDepartureTime()
        {
            return this.DepartureTime;
        }

        public int GetArrivalTime()
        {
            return this.ArrivalTime;
        }

        public int GetDestArrivalTime()
        {
            return this.DestArrivalTime;
        }

        public int GetDestDepartureTime()
        {
            return this.DestDepartureTime;
        }

        public override string GetRouteId()
        {
            return this.TimeTable.RouteId;
        }

        public override string GetRouteLongName()
        {
            return this.TimeTable.Entries.First().RouteLongName;
        }

        public override string GetRouteShortName()
        {
            return this.TimeTable.Entries.First().RouteShortName;
        }

        public override string GetServiceId()
        {
            return this.TimeTable.Entries.First().ServiceID;
        }

        /* TConnection */
        public override double GetTravelTime(string date, double arrivalTime, TravelMode Mode)
        {
            double time = 0;
            double trafficDelay = 0;

            if (!CanBeTraversedUsingMode(Mode)) return double.PositiveInfinity;

            if (Mode.HasFlag(Routing.TravelMode.Bus))
            {
                List<TimeTableEntry> Trips = TimeTable.GetFeasibleTrips(arrivalTime);

                if (Trips.Count == 0)
                    return double.PositiveInfinity;

                TimeTableEntry Trip = Trips.First();

                /* The "false" condition is added only for TEST A, in order to ignore the feed validity !!!REMEMBER TO REMOVE FOR TEST C!!! */
                if (/*(false) &&*/ (!Trip.IsValidDate(date)))
                    return double.PositiveInfinity;
                else
                {
                    time = (Trip.DestinationArrivalTime - Trip.SourceDepartureTime);

                    /* For Bus, the time is traffic dependent */
                    if ((TrafficReport != null) && (TimeTable.Entries.First().RouteType == RouteType.Bus))
                    {
                        double minDistanceFromTraffic;
                        minDistanceFromTraffic = Math.Min(this.TrafficDistanceFromSource, this.TrafficDistanceFromDestination);
                        trafficDelay = addTrafficDelay(time, minDistanceFromTraffic, TrafficReport);
                    }

                    /* Waiting time */
                    WaitingTime = (Trip.SourceDepartureTime - arrivalTime);

                    time += WaitingTime + trafficDelay;

                    DepartureTime = Trip.SourceDepartureTime;
                    ArrivalTime = Trip.SourceArrivalTime;
                    DestArrivalTime = Trip.DestinationArrivalTime;
                    DestDepartureTime = Trip.DestinationDepartureTime;

                    if (time < 0 || WaitingTime < 0)
                        throw new Exception("negative time value");
                }

            }
            else
            {
                time = double.PositiveInfinity;
            }
        

            return time;
        }

        public override bool CanBeTraversedUsingMode(TravelMode Mode)
        {
            return Mode.HasFlag(TravelMode.Bus);
        }

        public void AddTimeTableEntry(TimeTableEntry Entry)
        {
            TimeTable.AddEntry(Entry);
        }

        public override TimeTableEntry GetTimeTableEntry()
        {
            return TimeTable.Entries.First();
        }

        public override TimeTable GetTimeTable()
        {
            return TimeTable;
        }

        public override void setTrafficReport(TrafficReport TrafficReport)
        {
            this.TrafficReport = TrafficReport;
        }

        public override TrafficReport getTrafficReport()
        {
            return this.TrafficReport;
        }

        public override void setTrafficDistanceFromDestination(double trafficDistance)
        {
            this.TrafficDistanceFromDestination = trafficDistance;
        }

        public override double getTrafficDistanceFromDestination()
        {
            return this.TrafficDistanceFromDestination;
        }

        public override void setTrafficDistanceFromSource(double trafficDistance)
        {
            this.TrafficDistanceFromSource = trafficDistance;
        }

        public override double getTrafficDistanceFromSource()
        {
            return this.TrafficDistanceFromSource;
        }
        
        public override string GetCarpoolerId()
        {
            throw new NotImplementedException();
        }

        public override bool GetOneWay()
        {
            throw new NotImplementedException();
        }

        public override bool GetOnlyFoot()
        {
            throw new NotImplementedException();
        }
    }

    [Serializable]
    public class CConnection : Connection
    {
        public CNode Source { get; private set; }
        public CNode Destination { get; private set; }

        public double Distance { get; private set; }
        public int SrcArrivalTime { get; private set; }
        public int DstArrivalTime { get; private set; }
        public int DstDepartureTime { get; private set; }
        public double TravelTime { get; private set; }
        public double WaitingTime { get; private set; }
        public int ResidualCapacity { get; private set; }
        public int DepartureTime { get; private set; }

        public Carpools.Carpooler Carpooler { get; private set; }

        public TrafficReport TrafficReport { get; private set; }
        public double TrafficDistanceFromDestination { get; private set; }
        public double TrafficDistanceFromSource { get; private set; }
        public CCValidityStartDayTable CCValidityStartDayTable = new CCValidityStartDayTable();

        public CConnection(long id, CNode src, CNode dst, int srcArrivalTime, int dstArrivalTime, Carpools.Carpooler Pooler, Dictionary<string, string> tags)
            : base(id)
        {
            this.Source = src;
            this.Destination = dst;
            this.Distance = Source.Point.DistanceFrom(Destination.Point);
            this.DepartureTime = srcArrivalTime;
            this.SrcArrivalTime = srcArrivalTime;
            this.DstArrivalTime = dstArrivalTime;
            this.ResidualCapacity = Pooler.Capacity;
            this.Carpooler = Pooler;
            this.Tags = new Dictionary<string, string>(tags);
            ValidityDayTableEntry entry = new ValidityDayTableEntry(Pooler.Id, Pooler.TripDate, Pooler.Activated);
            CCValidityStartDayTable.Entries.Add(entry);
        }

        /* Just used for checking previous connections */
        public CConnection(long id, CNode src, CNode dst, int srcArrivalTime, int dstArrivalTime)
            : base(id)
        {
            this.Source = src;
            this.Destination = dst;
            this.Distance = Source.Point.DistanceFrom(Destination.Point);
            this.SrcArrivalTime = srcArrivalTime;
            this.DstArrivalTime = dstArrivalTime;
        }

        public override string GetCarpoolerId()
        {
            return Carpooler.Id;
        }

        public override double GetDistance(TravelMode Mode)
        {
            return this.Distance;
        }

        public override Node GetSource()
        {
            return this.Source;
        }

        public override Node GetDestination()
        {
            return this.Destination;
        }

        public override double GetWaitingTime()
        {
            return this.WaitingTime;
        }

        public override int GetDepartureTime()
        {
            return this.DepartureTime;
        }

        public double GetSrcArrivalTime()
        {
            return this.SrcArrivalTime;
        }

        public double GetDstArrivalTime()
        {
            return this.DstArrivalTime;
        }

        public override void setTrafficReport(TrafficReport TrafficReport)
        {
            this.TrafficReport = TrafficReport;
        }

        public override TrafficReport getTrafficReport()
        {
            return this.TrafficReport;
        }

        public override void setTrafficDistanceFromDestination(double trafficDistance)
        {
            this.TrafficDistanceFromDestination = trafficDistance;
        }

        public override double getTrafficDistanceFromDestination()
        {
            return this.TrafficDistanceFromDestination;
        }

        public override void setTrafficDistanceFromSource(double trafficDistance)
        {
            this.TrafficDistanceFromSource = trafficDistance;
        }

        public override double getTrafficDistanceFromSource()
        {
            return this.TrafficDistanceFromSource;
        }

        public override string GetRouteId()
        {
            throw new NotImplementedException();
        }

        public override string GetRouteLongName()
        {
            throw new NotImplementedException();
        }

        public override string GetRouteShortName()
        {
            throw new NotImplementedException();
        }

        public override string GetServiceId()
        {
            throw new NotImplementedException();
        }

        public override TimeTableEntry GetTimeTableEntry()
        {
            throw new NotImplementedException();
        }

        public override TimeTable GetTimeTable()
        {
            throw new NotImplementedException();
        }

        public override bool GetOneWay()
        {
            throw new NotImplementedException();
        }

        public override bool GetOnlyFoot()
        {
            throw new NotImplementedException();
        }

        /* CConnection */
        public override double GetTravelTime(string date, double arrivalTime, TravelMode Mode)
        {
            double time = 0;
            double trafficDelay = 0;

            if (!CanBeTraversedUsingMode(Mode)) return double.PositiveInfinity;
            if (arrivalTime > this.SrcArrivalTime) return double.PositiveInfinity;
            if (ResidualCapacity == 0) return double.PositiveInfinity;

            if (Mode.HasFlag(Routing.TravelMode.Carpool))
            {
                //ServiceDates.Contains(date)
                if (!CCValidityStartDayTable.IsValidDate(date, this.Carpooler.Id, this.Carpooler.Activated))
                    return double.PositiveInfinity;
                else
                {
                    time = (DstArrivalTime - SrcArrivalTime);

                    /* For Car, the time is traffic dependent */
                    if (TrafficReport != null)
                    {
                        double minDistanceFromTraffic;
                        minDistanceFromTraffic = Math.Min(this.TrafficDistanceFromSource, this.TrafficDistanceFromDestination);
                        trafficDelay = addTrafficDelay(time, minDistanceFromTraffic, TrafficReport);
                    }

                    WaitingTime = (SrcArrivalTime - arrivalTime);
                    time += WaitingTime + trafficDelay;

                    DepartureTime = (int)SrcArrivalTime;

                    if (time < 0 || WaitingTime < 0)
                        throw new Exception("negative time value");
                }
            }
            else
            {
                time = double.PositiveInfinity;
            }

            return time;
        }

        public override bool CanBeTraversedUsingMode(TravelMode Mode)
        {
            return Mode.HasFlag(TravelMode.Carpool);
        }
    }

    [Serializable]
    public class TConnectionForCarpooling : Connection
    {
        public TNode Source { get; private set; }
        public TNode Destination { get; private set; }
        public double Distance { get; private set; }
        public double WaitingTime { get; private set; }
        public int DepartureTime { get; private set; }
        public int ArrivalTime { get; private set; }
        public int DestArrivalTime { get; private set; }
        public int DestDepartureTime { get; private set; }

        public readonly TimeTable TimeTable;

        public TrafficReport TrafficReport { get; private set; }
        public double TrafficDistanceFromDestination { get; private set; }
        public double TrafficDistanceFromSource { get; private set; }

        public TConnectionForCarpooling(long id, TNode src, TNode dst)
            : base(id)
        {
            this.Source = src;
            this.Destination = dst;
            this.Distance = Source.Point.DistanceFrom(Destination.Point);
            this.TimeTable = new TimeTable();
            this.DepartureTime = -1;
            this.ArrivalTime = -1;
            this.DestArrivalTime = -1;
        }

        public override double GetDistance(TravelMode Mode)
        {
            return this.Distance;
        }

        public override Node GetSource()
        {
            return this.Source;
        }

        public override Node GetDestination()
        {
            return this.Destination;
        }

        public override double GetWaitingTime()
        {
            return this.WaitingTime;
        }

        public override int GetDepartureTime()
        {
            return this.DepartureTime;
        }

        public int GetArrivalTime()
        {
            return this.ArrivalTime;
        }

        public int GetDestArrivalTime()
        {
            return this.DestArrivalTime;
        }

        public int GetDestDepartureTime()
        {
            return this.DestDepartureTime;
        }

        public override string GetRouteId()
        {
            return this.TimeTable.RouteId;
        }

        public override string GetRouteLongName()
        {
            return this.TimeTable.Entries.First().RouteLongName;
        }

        public override string GetRouteShortName()
        {
            return this.TimeTable.Entries.First().RouteShortName;
        }

        public override string GetServiceId()
        {
            return this.TimeTable.Entries.First().ServiceID;
        }

        /* TConnectionForCarpooling */
        public override double GetTravelTime(string date, double arrivalTime, TravelMode Mode)
        {
            double time = 0;
            double trafficDelay = 0;

            if (Mode != TravelMode.Carpool)
            {
                if (!CanBeTraversedUsingMode(Mode)) return double.PositiveInfinity;


                if (Mode.HasFlag(Routing.TravelMode.Bus))
                {
                    List<TimeTableEntry> Trips = TimeTable.GetFeasibleTrips(arrivalTime);

                    if (Trips.Count == 0)
                        return double.PositiveInfinity;

                    TimeTableEntry Trip = Trips.First();

                    /* The "false" condition is added only for TEST A, in order to ignore the feed validity !!!REMEMBER TO REMOVE FOR TEST C!!! */
                    if (/*(false) &&*/ (!Trip.IsValidDate(date)))
                        return double.PositiveInfinity;
                    else
                    {
                        time = (Trip.DestinationArrivalTime - Trip.SourceDepartureTime);

                        /* For Bus, the time is traffic dependent */
                        if ((TrafficReport != null) && (TimeTable.Entries.First().RouteType == RouteType.Bus))
                        {
                            double minDistanceFromTraffic;
                            minDistanceFromTraffic = Math.Min(this.TrafficDistanceFromSource, this.TrafficDistanceFromDestination);
                            trafficDelay = addTrafficDelay(time, minDistanceFromTraffic, TrafficReport);
                        }

                        /* Waiting time */
                        WaitingTime = (Trip.SourceDepartureTime - arrivalTime);

                        time += WaitingTime + trafficDelay;

                        DepartureTime = Trip.SourceDepartureTime;
                        ArrivalTime = Trip.SourceArrivalTime;
                        DestArrivalTime = Trip.DestinationArrivalTime;
                        DestDepartureTime = Trip.DestinationDepartureTime;

                        if (time < 0 || WaitingTime < 0)
                            throw new Exception("negative time value");
                    }

                }
                else
                {
                    time = double.PositiveInfinity;
                }
            }
            else
            {
                int maxSpeed = 80;

                time = (Distance / 1000) / (float)(maxSpeed);
                time = time * 3600;

            }

            return time;
        }

        public override bool CanBeTraversedUsingMode(TravelMode Mode)
        {
            return Mode.HasFlag(TravelMode.Bus);
        }

        public void AddTimeTableEntry(TimeTableEntry Entry)
        {
            TimeTable.AddEntry(Entry);
        }

        public override TimeTableEntry GetTimeTableEntry()
        {
            return TimeTable.Entries.First();
        }

        public override TimeTable GetTimeTable()
        {
            return TimeTable;
        }

        public override void setTrafficReport(TrafficReport TrafficReport)
        {
            this.TrafficReport = TrafficReport;
        }

        public override TrafficReport getTrafficReport()
        {
            return this.TrafficReport;
        }

        public override void setTrafficDistanceFromDestination(double trafficDistance)
        {
            this.TrafficDistanceFromDestination = trafficDistance;
        }

        public override double getTrafficDistanceFromDestination()
        {
            return this.TrafficDistanceFromDestination;
        }

        public override void setTrafficDistanceFromSource(double trafficDistance)
        {
            this.TrafficDistanceFromSource = trafficDistance;
        }

        public override double getTrafficDistanceFromSource()
        {
            return this.TrafficDistanceFromSource;
        }

        public override string GetCarpoolerId()
        {
            throw new NotImplementedException();
        }

        public override bool GetOneWay()
        {
            throw new NotImplementedException();
        }

        public override bool GetOnlyFoot()
        {
            throw new NotImplementedException();
        }
    }
}
