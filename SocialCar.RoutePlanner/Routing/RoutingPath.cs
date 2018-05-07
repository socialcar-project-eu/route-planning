using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using SocialCar.RoutePlanner.Routing;
using SocialCar.RoutePlanner.Routing.Connections;
using SocialCar.RoutePlanner.Routing.Nodes;

namespace SocialCar.RoutePlanner.Routing
{
    public enum LegType
    {
        Foot = 1,
        Transport = 2,
        Carpool = 3,
        Car = 4
    }

    public class SolutionLeg
    {
        public List<Connection> Connections {get; private set;}

        public double StartTime {get; private set;}
        public double Duration {get; private set;} /* real duration without waiting times */
        public LegType LegType {get; private set;}
        public double WaitingTime { get; set; }

        public SolutionLeg(double StartTime, LegType Type)
        {
            this.Connections = new List<Connection>();
            this.StartTime = StartTime;
            this.Duration = 0;
            this.LegType = Type;
        }

        public void AddConnection(Connection C, double duration, double waitingTime)
        {
            this.Connections.Add(C);
            this.Duration += duration; // + waitingtime
            this.WaitingTime += waitingTime;
        }

        public void AddWaitingTimeToDuration(double duration)
        {
            this.Duration += duration;
        }
    }

    public class RoutingPath
    {
        public List<Connection> Connections { get; private set; }
        public List<SolutionLeg> Legs { get; private set; }
        public double StartTime { get; private set; }
        public string Date { get; private set; }
        public TravelMode Modes { get; private set; }

        public RoutingPath(List<Connection> Connections, string Date, double StartTime, TravelMode Modes)
        {
            this.Connections = new List<Connection>();

            this.Connections.AddRange(Connections);
            this.Legs = new List<SolutionLeg>();
            this.Date = Date;
            this.StartTime = StartTime;
            this.Modes = Modes;

            this.ConstructLegs();
        }

        private void ConstructLegs()
        {
            double Time = this.StartTime;
            Connection C = null;
            Connection C_prev = null;

            LegType Type = LegType.Foot;
            if (Modes == TravelMode.Car)
                Type = LegType.Car;

            SolutionLeg Leg = new SolutionLeg(StartTime, Type);
            SolutionLeg LegPrev = Leg;

            Legs.Add(Leg);
            
            for (int i = 0; i < Connections.Count; ++i)
            {
                C = Connections[i];

                if ( (C is LConnection) && (i < Connections.Count-1) )
                {
                    AddNewLeg(Type, Connections[i + 1], ref LegPrev, ref Leg, Time);
                }
                else if (Modes == TravelMode.Carpool)
                {
                    double TTime;

                    TTime = C.GetTravelTime(Date, Time, Modes);

                    Leg.AddConnection(C, TTime, C.GetWaitingTime());

                    Time += TTime;
                }
                else
                {
                    double TTime, TTimeWithWaitings;

                    TTime = C.GetTravelTime(Date, Time, Modes);
                    TTimeWithWaitings = TTime;

                    /* Remove waiting times from the single legs */
                    TTime -= C.GetWaitingTime();
                    if (TTime < 0)
                    {
                        //TODO in rare cases TTime is negative, unfortunately this is not deterministic. Instead to make the RP crash set to 0 the TTime for this connection.
                        TTime = 0;
                        //throw new Exception("negative time value");
                    }

                    Leg.AddConnection(C, TTime, C.GetWaitingTime());
                    //Leg.AddConnection(C, TTime);

                    TTime = TTimeWithWaitings;
                    Time += TTime;
                    
                    /* If there are 2 adjacent TConnections, split them */
                    if ( (i < Connections.Count - 1) && (Connections[i + 1] is TConnection) )
                        //if (String.Compare(C.GetRouteId(), Connections[i + 1].GetRouteId()) != 0)
                        if ( (String.Compare(C.GetRouteShortName(), Connections[i + 1].GetRouteShortName()) != 0) ||
                            (String.Compare(C.GetRouteLongName(), Connections[i + 1].GetRouteLongName()) != 0) )
                        {
                            AddNewLeg(Type, Connections[i + 1], ref LegPrev, ref Leg, Time);
                        }
                            
                }

                C_prev = C;
            }

        }

        private void AddNewLeg(LegType Type, Connection C, ref SolutionLeg LegPrev, ref SolutionLeg Leg, double Time)
        {
            if (C is RConnection)
                Type = LegType.Foot;
            else if (C is TConnection)
                Type = LegType.Transport;
            else if (C is CConnection)
                Type = LegType.Carpool;

            LegPrev = Leg;
            Leg = new SolutionLeg(Time, Type);
            Legs.Add(Leg);
        }

    }
}
