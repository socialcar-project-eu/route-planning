using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using log4net;
using log4net.Config;
using SocialCar.RoutePlanner.Routing;
using SocialCar.RoutePlanner.Routing.Nodes;
using System.Runtime.Serialization;
using System.Threading;
using System.Text.RegularExpressions;
using System.Configuration;

namespace SocialCar.RoutePlanner.WebAPI
{
    static class RequestHandler
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public static readonly string voidJson = "[{}]";

        public static readonly int SOLUTION_NUMBER = 5;
        public static readonly int MAX_THREAD_NUMBER = 50;
        public static readonly int SECONDS_FORWARD = 5 * 60;
        public static readonly string stringSeparator = "###";
        public static readonly int WAITING_TIME_LIMIT = 1800;
        public static readonly int MIN_WAITING_TIME = 120;
        public static readonly int THRESHOLD_FUTURE_TRIPS = 60*60*4; //secs

        public static readonly int MAX_WALKING_DISTANCE_THRESHOLD = 5000; //[m]
        public static readonly int MAX_DIST_FROM_DEST = 1500; //[m] //used to set a limit (as the crow flies) in order to calculate the real distance

        public static void checkParams(Dictionary<string, string> Params)
        {

        }

        public static Message HandleRouteRequest(Dictionary<string, string> Params, HttpListenerContext context, RoutingNetwork productionRoutingNetwork)
        {
            double TrafficPropagationMaxDistance = int.Parse(ConfigurationManager.AppSettings["TrafficPropagationMaxDistance"]);
            Message Message = new Message { };

            try
            {
                checkParams(Params);

                string JSONResponse = string.Empty;

                float delta = 0.1f;

                Point DeparturePoint = new Point(double.Parse(Params["slat"]), double.Parse(Params["slng"]));
                Point DestinationPoint = new Point(double.Parse(Params["tlat"]), double.Parse(Params["tlng"]));

                string DateString = Params["date"];
                string TimeString = Params["time"];

                DateTime requestDateTime = Globals.ConvertDateAndTimeToLocalSiteDateTime(DateString, TimeString);

                //Seconds since midnight of the http request
                double Time = (requestDateTime.Hour * 3600) + (requestDateTime.Minute * 60) + requestDateTime.Second;

                TravelMode Modes = TravelMode.Walking | TravelMode.Bus | TravelMode.Carpool;



                List<RNode> ListNode = new List<RNode> { };
                ListNode = productionRoutingNetwork.ResolvePoint(DeparturePoint, delta);
                if (ListNode.Count() <= 0)
                {
                    /* TODO CREATE FUNCTION FOR SAVING THE ERROR PACKET */
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    Message.Result = false;
                    Message.Error.Message = Message.Error.GetDescriptionFromEnumValue(ErrorCodes.DEPARTURE_POINT);
                    Message.Error.Code = Message.Error.GetDefaultValueAttributeFromEnumValue(ErrorCodes.DEPARTURE_POINT);
                    Message.Error.Params.Add(nameof(DeparturePoint.Latitude), DeparturePoint.Latitude.ToString());
                    Message.Error.Params.Add(nameof(DeparturePoint.Longitude), DeparturePoint.Longitude.ToString());
                    return Message;
                }
                RNode StartNode = ListNode.First();


                ListNode = productionRoutingNetwork.ResolvePoint(DestinationPoint, delta);
                if (ListNode.Count() <= 0)
                {
                    /* TODO CREATE FUNCTION FOR SAVING THE ERROR PACKET */
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    Message.Result = false;
                    Message.Error.Message = Message.Error.GetDescriptionFromEnumValue(ErrorCodes.DESTINATION_POINT);
                    Message.Error.Code = Message.Error.GetDefaultValueAttributeFromEnumValue(ErrorCodes.DESTINATION_POINT);
                    Message.Error.Params.Add(nameof(DestinationPoint.Latitude), DestinationPoint.Latitude.ToString());
                    Message.Error.Params.Add(nameof(DestinationPoint.Longitude), DestinationPoint.Longitude.ToString());
                    return Message;
                }
                RNode TargetNode = ListNode.First();

                ///* Find the PT stops list nearby */
                //List<TNodeCarpooling> PTstops = productionRoutingNetwork.findClosestStop(StartNode.Point);

                /* Get Router Factors from config file */
                //TODO

                /* Router is called SOLUTION_NUMBER times by using different threads */
                List<Router> routerList = new List<Router> { };

                /*
                 * Params order: WalkingFactor, TransportationFactor, TransportationChangeFactor, CarpoolingFactor
                 */
                int solution_number = 5;
                int solution_num_list = 3;
                int solution_tot_number = solution_number * solution_num_list;
                Thread[] threadsArray = new Thread[solution_tot_number];
                Router.RouterFactors[] routerFactorsList = new Router.RouterFactors[solution_num_list];

                //routerFactorsList[0] = new Router.RouterFactors(30, 1, 20, (float)0.5, TrafficPropagationMaxDistance, 0, SECONDS_FORWARD); //Test M

                routerFactorsList[0] = new Router.RouterFactors(30, 1, 20, (float)0.5, TrafficPropagationMaxDistance, 0, SECONDS_FORWARD); //J
                routerFactorsList[1] = new Router.RouterFactors(30, 1, 20, (float)50, TrafficPropagationMaxDistance, 1, SECONDS_FORWARD); //Penalize Carpooling (in order to have the same solution without CP), WARNING: setting a carpooling value >=50 means that Mobalt busses will be skipped for this weights combo (value hardcoded into the Connections.cs file)
                routerFactorsList[2] = new Router.RouterFactors(20, 1, 50, (float)0.5, TrafficPropagationMaxDistance, 2, SECONDS_FORWARD); //M1

                //TEST new transportation factor (the value is related to the seconds added for each change)
                //routerFactorsList[0] = new Router.RouterFactors(30, 1, 300, (float)0.5, TrafficPropagationMaxDistance, 0, SECONDS_FORWARD); //J
                //routerFactorsList[1] = new Router.RouterFactors(30, 1, 600, (float)0.5, TrafficPropagationMaxDistance, 0, SECONDS_FORWARD); //J
                //routerFactorsList[2] = new Router.RouterFactors(30, 1, 0, (float)0.5, TrafficPropagationMaxDistance, 0, SECONDS_FORWARD); //J

                //old
                //routerFactorsList[2] = new Router.RouterFactors(15, 1, 50, (float)0.5, TrafficPropagationMaxDistance); //M
                //routerFactorsList[] = new Router.RouterFactors( 5, 10, 50, (float)0.5, TrafficPropagationMaxDistance); //M: bad results (go down from a PT, walk, and get back on the same PT on the next stop)
                //routerFactorsList[] = new Router.RouterFactors( 5, 10, 30, (float)0.5, TrafficPropagationMaxDistance); //F: bad results (go down from a PT, walk, and get back on the same PT on the next stop)
                //routerFactorsList[3] = new Router.RouterFactors(10, 1, 50, (float)0.5, TrafficPropagationMaxDistance, 3, SECONDS_FORWARD); //M

                for (int n = 0; n < solution_num_list; n++)
                { 
                    for (int i = 0; i < solution_number; i++)
                    {
                        int index = (solution_number * n) + i;
                        routerList.Add(new Router(StartNode, TargetNode, DateString, Time + (i * SECONDS_FORWARD), Modes, ObjFunction.Weighted, i, routerFactorsList[n]));
                        threadsArray[index] = new Thread(new ThreadStart(routerList.Last().Start));
                        threadsArray[index].Start();
                    }
                }

                /* Wait all threads end */
                for (int i = 0; i < solution_tot_number; i++)
                    while (threadsArray[i].IsAlive) ;

                //OBSOLETE
                //else
                //{
                //    Thread[] threadsArray = new Thread[MAX_THREAD_NUMBER];
                //    Router.RouterFactors[] routerFactorsList = new Router.RouterFactors[SOLUTION_NUMBER];
                //    routerFactorsList[0] = new Router.RouterFactors(30, 1, 20, (float)0.5, TrafficPropagationMaxDistance, 0, 0); //J
                //    routerFactorsList[1] = new Router.RouterFactors(5, 15, 30, (float)0.5, TrafficPropagationMaxDistance, 0, 0); //F
                //    routerFactorsList[2] = new Router.RouterFactors(25, 1, 50, (float)0.5, TrafficPropagationMaxDistance, 0, 0); //M1
                //    routerFactorsList[3] = new Router.RouterFactors(30, 20, 40, (float)0.5, TrafficPropagationMaxDistance, 0, 0); //M2
                //    routerFactorsList[4] = new Router.RouterFactors(10, 20, 40, (float)0.5, TrafficPropagationMaxDistance, 0, 0); //M3

                //    for (int i = 0; i < SOLUTION_NUMBER; i++)
                //    {
                //        routerList.Add(new Router(StartNode, TargetNode, DateString, Time, Modes, ObjFunction.Weighted, routerFactorsList[i]));
                //        threadsArray[i] = new Thread(new ThreadStart(routerList.Last().Start));
                //        threadsArray[i].Start();
                //    }

                //    /* Wait all threads end */
                //    for (int i = 0; i < SOLUTION_NUMBER; i++)
                //        while (threadsArray[i].IsAlive) ;
                //}

                foreach (Router router in routerList)
                {
                    foreach (RoutingPath routingPath in router.RoutingPaths)
                    {
                        List<Contracts.Trip> Trips = new List<Contracts.Trip>();

                        Contracts.Trip Trip = new Contracts.Trip();

                        Contracts.Transport Transport = null;
                        Contracts.Route Route = null;

                        DateTime GlobalDate = new DateTime(int.Parse(DateString.Substring(0, 4)), int.Parse(DateString.Substring(4, 2)), int.Parse(DateString.Substring(6, 2)));

                        double distance = 0.0;
                        int stops = 0;
                        string departureTime = string.Empty;
                        string address = string.Empty;
                        string prevAddress = string.Empty;
                        string stop_id = string.Empty;
                        double tripDuration = 0.0;

                        int j = 0;

                        //foreach (SolutionLeg Leg in routingPath.Legs)
                        int i = 0;
                        for (i=0; i< routingPath.Legs.Count; i++)
                        {
                            SolutionLeg Leg = routingPath.Legs[i];
                            SolutionLeg Legnext = null;
                            SolutionLeg Legprev = null;

                            if (i < routingPath.Legs.Count - 1)
                            {
                                Legnext = routingPath.Legs[i + 1];
                                /* This is used to get the next next leg if the next one is a feet leg with 0 connections */
                                if (((Legnext == null) || (Legnext.Connections.Count == 0)) && (i < routingPath.Legs.Count - 2))
                                    Legnext = routingPath.Legs[i + 2];
                            }

                            Transport = new Contracts.Transport();
                            stops = 0;
                            stop_id = string.Empty;
                            departureTime = string.Empty;

                            /* Fix to correct the departure time of a FEET leg, based on the starting time of the subseguent mean of transport */
                            if ( (i == 0) && (Legnext != null) && (Leg.LegType == LegType.Foot) )
                            {
                                departureTime = ((Legnext.StartTime - Leg.Duration) - MIN_WAITING_TIME).ToString(); //let the user 2 minutes before
                                Legnext.WaitingTime = MIN_WAITING_TIME;
                            }

                            /* Fix to correct the wrong departure time for the last foot connection, based on the arrival time of the previous mean of transport */
                            if ((i == (routingPath.Legs.Count-1)) && (Leg.LegType == LegType.Foot) && (Legprev != null))
                            {
                                departureTime = (Legprev.StartTime + Legprev.Duration).ToString(); //let the user start walking as soon as the last mean of transport arrive to destination
                            }

                            //var route_id = ((Routing.Connections.TConnection)Leg.Connections.First()).TimeTable.Entries.First().RouteId;

                            if (Leg.Connections.Count > 0)
                            {
                                if (Leg.LegType == LegType.Foot)
                                    Transport.TravelMode = "FEET";
                                else if (Leg.LegType == LegType.Carpool)
                                    Transport.TravelMode = "CAR_POOLING"; 
                                else if (Leg.LegType == LegType.Car)
                                    Transport.TravelMode = "CAR";

                                if (Leg.LegType == LegType.Foot || Leg.LegType == LegType.Carpool || Leg.LegType == LegType.Car)
                                {
                                    address = Leg.Connections.Last().GetTagValue("addr:housenumber") + "," + Leg.Connections.Last().GetTagValue("addr:street");
                                    if (Leg.Connections.Last().GetTagValue("addr:street") == null)
                                        address = Leg.Connections.Last().GetTagValue("name");
                                    
                                    Transport.LongName = address;
                                    Transport.ShortName = address;

                                    if (Leg.LegType == LegType.Foot)
                                        distance = Leg.Connections.Sum(x => x.GetDistance(TravelMode.Walking));
                                    else
                                        distance = Leg.Connections.Sum(x => x.GetDistance(TravelMode.Car));

                                    if (Leg.LegType == LegType.Carpool)
                                    {
                                        Transport.RideID = ((Routing.Connections.CConnection)Leg.Connections.First()).Carpooler.Id.ToString();
                                        Transport.RideName = ((Routing.Connections.CConnection)Leg.Connections.First()).Carpooler.Name;
                                    }

                                }
                                else if (Leg.LegType == LegType.Transport)
                                {
                                    Transport.TravelMode = ((Routing.Connections.TConnection)Leg.Connections.First()).TimeTable.RouteType.ToString().ToUpper();
                                    Transport.LongName = ((Routing.Connections.TConnection)Leg.Connections.First()).TimeTable.Entries.First().RouteLongName;
                                    Transport.ShortName = ((Routing.Connections.TConnection)Leg.Connections.First()).TimeTable.Entries.First().RouteShortName;
                                    Transport.AgencyID = Regex.Split(((Routing.Connections.TConnection)Leg.Connections.First()).TimeTable.Entries.First().AgencyId, stringSeparator).Last();
                                    if (string.Compare(Transport.AgencyID, "Mobalt") == 0)
                                        Transport.TravelMode = "CAR_POOLING";
                                    Transport.RouteID = Regex.Split(((Routing.Connections.TConnection)Leg.Connections.First()).TimeTable.Entries.First().RouteId, stringSeparator).Last();
                                    Transport.RouteUrl = ((Routing.Connections.TConnection)Leg.Connections.First()).TimeTable.Entries.First().RouteUrl;
                                    Transport.TripId = Regex.Split(((Routing.Connections.TConnection)Leg.Connections.First()).TimeTable.Entries.First().TripId, stringSeparator).Last();

                                    if (string.Compare(Transport.LongName,"") == 0)
                                        Transport.LongName = ((Routing.Connections.TConnection)Leg.Connections.First()).TimeTable.Entries.First().RouteDesc + " " + Transport.ShortName;

                                    distance = Leg.Connections.Sum(x => x.GetDistance(TravelMode.Bus));
                                    stops = Leg.Connections.Count() + 1;
                                }

                                Route = new Contracts.Route();

                                Contracts.Point Point = null;
                                //foreach (Routing.Connections.Connection C in Leg.Connections)
                                int k = 0;
                                bool alreadyset = true;
                                for (k=0; k<Leg.Connections.Count; k++)
                                { 
                                    Routing.Connections.Connection C = Leg.Connections[k];
                                    Routing.Connections.Connection Cnext = null;
                                    if (k < Leg.Connections.Count - 1)
                                        Cnext = Leg.Connections[k + 1];

                                    if (Route.Points.Count == 0)
                                    {
                                        departureTime = RoutePlanner.Globals.GetUnixTimeStamp(GlobalDate.AddSeconds(C.GetDepartureTime())).ToString();

                                        if (C is Routing.Connections.RConnection)
                                        {
                                            address = C.GetTagValue("addr:housenumber") + "," + C.GetTagValue("addr:street");
                                            if (C.GetTagValue("addr:street") == null)
                                                address = C.GetTagValue("name");
                                            alreadyset = false;
                                        }
                                        else if (C is Routing.Connections.TConnection)
                                        {
                                            address = ((Routing.Nodes.TNode)C.GetSource()).StopName;
                                            stop_id = Regex.Split(((Routing.Nodes.TNode)C.GetSource()).StopId, stringSeparator).Last();

                                            departureTime = RoutePlanner.Globals.GetUnixTimeStamp(GlobalDate.AddSeconds(((Routing.Connections.TConnection)C).GetDepartureTime())).ToString();
                                            alreadyset = false;
                                        }
                                        else if (C is Routing.Connections.CConnection)
                                        {
                                            address = C.GetTagValue("addr:housenumber") + "," + C.GetTagValue("addr:street");
                                            if (C.GetTagValue("addr:street") == null)
                                                address = C.GetTagValue("name");

                                            address = ((Routing.Nodes.CNode)C.GetSource()).StopName;

                                            stop_id = C.GetCarpoolerId().ToString();

                                            /* If this is the last connection of this Leg and the next leg is a public transport, get the stop name as address (in order to show it to the app) */
                                            if ((k == Leg.Connections.Count - 1) && (Legnext != null) && (Legnext.Connections.Count != 0) && (Legnext.Connections[0] is Routing.Connections.TConnection))
                                                address = ((Routing.Nodes.TNode)Legnext.Connections[0].GetSource()).StopName;

                                            if (address == null)
                                            {
                                                //bad: this will provide a code number and the app will show this, but we do not have information about the start address, just coordinates, and they are already available into the connection
                                                address = ((Routing.Nodes.RNode)C.GetSource()).Id.ToString();
                                                address = prevAddress;
                                            }
                                            alreadyset = false;
                                        }

                                        if (!alreadyset)
                                        {
                                            Point = new Contracts.Point
                                            {
                                                Coordinates = new Contracts.Coordinates
                                                {
                                                    Latitude = C.GetSource().Point.Latitude.ToString(),
                                                    Longitude = C.GetSource().Point.Longitude.ToString(),
                                                },

                                                Date = RoutePlanner.Globals.GetUnixTimeStamp(GlobalDate.AddSeconds(Leg.StartTime)).ToString(),
                                                DepartureTime = departureTime,
                                                Address = address,
                                                StopId = stop_id,
                                            };

                                            Route.Points.Add(Point);

                                            alreadyset = true;
                                        }

                                        if (C is Routing.Connections.RConnection)
                                        {
                                            address = C.GetTagValue("addr:housenumber") + "," + C.GetTagValue("addr:street");
                                            if (C.GetTagValue("addr:street") == null)
                                                address = C.GetTagValue("name");

                                            alreadyset = false;
                                        }
                                        else if (C is Routing.Connections.TConnection)
                                        {
                                            address = ((Routing.Nodes.TNode)C.GetDestination()).StopName;
                                            stop_id = Regex.Split(((Routing.Nodes.TNode)C.GetDestination()).StopId, stringSeparator).Last();

                                            departureTime = RoutePlanner.Globals.GetUnixTimeStamp(GlobalDate.AddSeconds(((Routing.Connections.TConnection)C).GetDestDepartureTime())).ToString();

                                            alreadyset = false;
                                        }
                                        else if (C is Routing.Connections.CConnection)
                                        {
                                            address = C.GetTagValue("addr:housenumber") + "," + C.GetTagValue("addr:street");
                                            if (C.GetTagValue("addr:street") == null)
                                                address = C.GetTagValue("name");

                                            address = ((Routing.Nodes.CNode)C.GetDestination()).StopName;

                                            stop_id = C.GetCarpoolerId().ToString();

                                            /* If this is the last connection of this Leg and the next leg is a public transport, get the stop name as address (in order to show it to the app) */
                                            if ((k == Leg.Connections.Count - 1) && (Legnext != null) && (Legnext.Connections.Count != 0) && (Legnext.Connections[0] is Routing.Connections.TConnection))
                                                address = ((Routing.Nodes.TNode)Legnext.Connections[0].GetSource()).StopName;

                                            if (address == null)
                                            {
                                                //bad: this will provide a code number and the app will show this
                                                address = ((Routing.Nodes.RNode)C.GetDestination()).Id.ToString();
                                                address = prevAddress;
                                            }

                                            alreadyset = false;

                                            ///* Logic to set the destination address for the carpooling rides (which do not have one by default) */
                                            //// if after a CConnection there is a Tconnection, set the final address as the Tconnection StopName
                                            //if ((k == (Leg.Connections.Count - 1)) && (i < (routingPath.Legs.Count - 1)) && (Legnext is Routing.Connections.TConnection))
                                            //    address = ((Routing.Nodes.TNode)Legnext.Connections[0].GetSource()).StopName;
                                            //// if the CConnection is the last leg set the final address as the trip destination 
                                            //else if ((k == (Leg.Connections.Count - 2)) && (i == (routingPath.Legs.Count - 1)))
                                            //{
                                            //    //but we do not have information about the destination address, just coordinates, and they are already available into the connection
                                            //}
                                        }

                                        if (!alreadyset)
                                        {
                                            Point = new Contracts.Point
                                            {
                                                Coordinates = new Contracts.Coordinates
                                                {
                                                    Latitude = C.GetDestination().Point.Latitude.ToString(),
                                                    Longitude = C.GetDestination().Point.Longitude.ToString(),
                                                },

                                                Date = RoutePlanner.Globals.GetUnixTimeStamp(GlobalDate.AddSeconds(Leg.StartTime)).ToString(),
                                                DepartureTime = departureTime,
                                                Address = address,
                                                StopId = stop_id,
                                            };

                                            Route.Points.Add(Point);

                                            alreadyset = true;
                                        }
                                    }
                                    else
                                    {
                                        departureTime = RoutePlanner.Globals.GetUnixTimeStamp(GlobalDate.AddSeconds(C.GetDepartureTime())).ToString();

                                        if (C is Routing.Connections.RConnection)
                                        {
                                            address = C.GetTagValue("addr:housenumber") + "," + C.GetTagValue("addr:street");
                                            if (C.GetTagValue("addr:street") == null)
                                                address = C.GetTagValue("name");

                                            alreadyset = false;
                                        }
                                        else if (C is Routing.Connections.TConnection)
                                        {
                                            address = ((Routing.Nodes.TNode)C.GetDestination()).StopName;
                                            stop_id = Regex.Split(((Routing.Nodes.TNode)C.GetDestination()).StopId, stringSeparator).Last();

                                            departureTime = RoutePlanner.Globals.GetUnixTimeStamp(GlobalDate.AddSeconds(((Routing.Connections.TConnection)C).GetDestDepartureTime())).ToString();

                                            alreadyset = false;
                                        }
                                        else if (C is Routing.Connections.CConnection)
                                        {
                                            address = C.GetTagValue("addr:housenumber") + "," + C.GetTagValue("addr:street");
                                            if (C.GetTagValue("addr:street") == null)
                                                address = C.GetTagValue("name");

                                            address = ((Routing.Nodes.CNode)C.GetDestination()).StopName;

                                            stop_id = C.GetCarpoolerId().ToString();

                                            /* If this is the last connection of this Leg and the next leg is a public transport, get the stop name as address (in order to show it to the app) */
                                            if ( (k == Leg.Connections.Count - 1) && (Legnext != null) && (Legnext.Connections.Count != 0) && (Legnext.Connections[0] is Routing.Connections.TConnection) )
                                                address = ((Routing.Nodes.TNode)Legnext.Connections[0].GetSource()).StopName;

                                            if (address == null)
                                            {
                                                //bad: this will provide a code number and the app will show this
                                                address = ((Routing.Nodes.RNode)C.GetDestination()).Id.ToString();
                                                address = prevAddress;
                                            }

                                            alreadyset = false;

                                            ///* Logic to set the destination address for the carpooling rides (which do not have one by default) */
                                            //// if after a CConnection there is a Tconnection, set the final address as the Tconnection StopName
                                            //if ((k == (Leg.Connections.Count - 1)) && (i < (routingPath.Legs.Count - 1)) && (Legnext is Routing.Connections.TConnection))
                                            //    address = ((Routing.Nodes.TNode)Legnext.Connections[0].GetSource()).StopName;
                                            //// if the CConnection is the last leg set the final address as the trip destination 
                                            //else if ((k == (Leg.Connections.Count - 2)) && (i == (routingPath.Legs.Count - 1)))
                                            //{
                                            //    //but we do not have information about the destination address, just coordinates, and they are already available into the connection
                                            //}
                                        }


                                        /* Fix for the "Unknown address" issue: if the address is null get the next PT Source stop address */
                                        if (address == null)
                                        {
                                            if ((Leg.LegType == LegType.Foot) && (C == Leg.Connections.Last()) && (j < (routingPath.Legs.Count() - 1)))
                                            {
                                                if ( (routingPath.Legs[j + 1].Connections.Count > 0) && (routingPath.Legs[j + 1].Connections[0].GetType() == typeof(Routing.Connections.TConnection)))
                                                {
                                                    address = ((Routing.Nodes.TNode)routingPath.Legs[j+1].Connections[0].GetSource()).StopName;
                                                    Transport.LongName = address;
                                                    Transport.ShortName = address;
                                                }
                                            }
                                        }

                                        prevAddress = address;


                                        if (!alreadyset)
                                        {
                                            Point = new Contracts.Point
                                            {
                                                Coordinates = new Contracts.Coordinates
                                                {
                                                    Latitude = C.GetDestination().Point.Latitude.ToString(),
                                                    Longitude = C.GetDestination().Point.Longitude.ToString(),
                                                },

                                                Date = RoutePlanner.Globals.GetUnixTimeStamp(GlobalDate.AddSeconds(Leg.StartTime)).ToString(),
                                                DepartureTime = departureTime,
                                                Address = address,
                                                StopId = stop_id,
                                            };

                                            Route.Points.Add(Point);

                                            alreadyset = true;
                                        }
                                    }
                                }


                                Trip.RouterFactors = router.RI.routerFactors;
                                Trip.Legs.Add(new Contracts.Leg
                                {
                                    Transport = Transport,
                                    //WRONG DepartureTime = RoutePlanner.Globals.GetUnixTimeStamp(GlobalDate.AddSeconds(Leg.StartTime)).ToString(),
                                    //DepartureTime = RoutePlanner.Globals.GetUnixTimeStamp(GlobalDate.AddSeconds(Leg.Connections[0].GetDepartureTime())).ToString(),
                                    DepartureTime = Route.Points.First().DepartureTime,
                                    Distance = distance.ToString(),
                                    Stops = stops.ToString(),
                                    Duration = Leg.Duration.ToString(),
                                    WaitingTime = Leg.WaitingTime.ToString(),
                                    Route = Route,
                                });

                                tripDuration += Leg.Duration;
                            }

                            Legprev = Leg;
                            j++;
                        }

                        /* Fix: solve issue related the bad departure time for the first FEET legs */
                        if ((Trip.Legs.Count >= 2) && (string.Compare(Trip.Legs[0].Transport.TravelMode, "FEET") == 0))
                        {
                            string LegDepartureTime = Math.Round((int.Parse(Trip.Legs[1].DepartureTime) - double.Parse(Trip.Legs[0].Duration))).ToString();
                            Trip.Legs[0].DepartureTime = LegDepartureTime;
                        }

                        /* Fix: solve issue related the bad departure time for the last FEET legs */
                        if ((Trip.Legs.Count >= 2) && (string.Compare(Trip.Legs[Trip.Legs.Count - 1].Transport.TravelMode, "FEET") == 0))
                        {
                            string LegDepartureTime = Math.Round((int.Parse(Trip.Legs[Trip.Legs.Count-2].DepartureTime) + double.Parse(Trip.Legs[Trip.Legs.Count-2].Duration))).ToString();
                            Trip.Legs[Trip.Legs.Count-1].DepartureTime = LegDepartureTime;
                        }

                        //old Frontend calculation, wrong Trip.TripDuration = (int.Parse(Trip.Legs.Last().DepartureTime) + double.Parse(Trip.Legs.Last().Duration)) - (int.Parse(Trip.Legs.First().DepartureTime));
                        Trip.TripDuration = tripDuration;

                        /* Add trip to the message */
                        Message.Trips.Add(Trip);

                        /* Add RP internal messages */
                        Message.Error.RPMessage = router.getRPMessage();

                    }
                }


                /* --POST PROCESSING-- */
                /* Remove duplicates from the solution list */
                for (int k = 0; k < Message.Trips.Count() - 1; k++)
                {
                    for (int m = k + 1; m < Message.Trips.Count(); m++)
                    {
                        bool newTrip = false;
                        if (Message.Trips[k].Legs.Count() == Message.Trips[m].Legs.Count())
                        {
                            int l = 0;
                            while ((l < Message.Trips[k].Legs.Count()) && (newTrip == false))
                            {
                                if (Message.Trips[k].Legs[l].Transport.TravelMode.Equals(Message.Trips[m].Legs[l].Transport.TravelMode) == true)
                                {
                                    if (((Message.Trips[k].Legs[l].Transport.LongName != null) &&
                                            (Message.Trips[m].Legs[l].Transport.LongName != null) &&
                                            (Message.Trips[k].Legs[l].Transport.LongName.Equals(Message.Trips[m].Legs[l].Transport.LongName) == false)) ||

                                         ((Message.Trips[k].Legs[l].Transport.ShortName != null) &&
                                            (Message.Trips[m].Legs[l].Transport.ShortName != null) &&
                                            (Message.Trips[k].Legs[l].Transport.ShortName.Equals(Message.Trips[m].Legs[l].Transport.ShortName) == false)) ||

                                         (Message.Trips[k].Legs[l].Transport.RouteID != Message.Trips[m].Legs[l].Transport.RouteID) ||

                                         (Message.Trips[k].Legs[l].Transport.TripId != Message.Trips[m].Legs[l].Transport.TripId))
                                    {
                                        /* If the connection name or id are different */
                                        newTrip = true;
                                    }
                                }
                                else
                                {
                                    /* If the transport mode is different */
                                    newTrip = true;
                                }

                                l++;
                            }

                            if (newTrip == false)
                            {
                                Message.Trips[m].Info = "Duplicated";
                                Message.TripsRemoved.Add(Message.Trips[m]);
                                Message.Trips.Remove(Message.Trips[m]);
                                m--;
                            }
                        }
                    }
                }


                /* Remove trips which match these constraints:
                 * - includes more than 1 carpooling ride
                 * - walking distance > 5km
                 * - waiting time > 1800s
                 * and count how many PT for each trip
                 */
                for (int k = 0; k < Message.Trips.Count(); k++)
                {
                    int m = 0;
                    int countCarPoolingRides = 0;
                    bool tooFarAwayByFeet = false;
                    bool deleted = false;
                    bool waitingTimeTooLong = false;

                    for (m = 0; m < Message.Trips[k].Legs.Count(); m++)
                    {
                        if (String.Compare(Message.Trips[k].Legs[m].Transport.TravelMode, "CAR_POOLING") == 0)
                        {
                            countCarPoolingRides++;
                        }

                        if (String.Compare(Message.Trips[k].Legs[m].Transport.TravelMode, "FEET") == 0)
                        {
                            if (float.Parse(Message.Trips[k].Legs[m].Distance) > MAX_WALKING_DISTANCE_THRESHOLD)
                            {
                                tooFarAwayByFeet = true;
                                break;
                            }
                        }

                        if (float.Parse(Message.Trips[k].Legs[m].WaitingTime) > WAITING_TIME_LIMIT)
                        {
                            waitingTimeTooLong = true;
                            break;
                        }


                        if ((String.Compare(Message.Trips[k].Legs[m].Transport.TravelMode, "CAR_POOLING") != 0) &&
                            (String.Compare(Message.Trips[k].Legs[m].Transport.TravelMode, "FEET") != 0))
                            Message.Trips[k].CountPT++;
                    }

                    if (countCarPoolingRides > 1)
                    {
                        Message.Trips[k].Info = "Too much carpooling rides";
                        Message.TripsRemoved.Add(Message.Trips[k]);
                        Message.Trips.Remove(Message.Trips[k]);
                        k--;
                        deleted = true;
                    }

                    if ((tooFarAwayByFeet == true) && (!deleted))
                    {
                        Message.Trips[k].Info = "Walking distance too high (" + Message.Trips[k].Legs[m].Distance + ")";
                        Message.TripsRemoved.Add(Message.Trips[k]);
                        Message.Trips.Remove(Message.Trips[k]);
                        k--;
                        deleted = true;
                    }

                    if ((waitingTimeTooLong == true) && (!deleted))
                    {
                        Message.Trips[k].Info = "Waiting time too long (" + Message.Trips[k].Legs[m].Duration + ")";
                        Message.TripsRemoved.Add(Message.Trips[k]);
                        Message.Trips.Remove(Message.Trips[k]);
                        k--;
                        deleted = true;
                    }
                }


                /* Remove trips which includes too many PT (only if there are at least 2 solutions with few PTs) */
                if (Message.Trips.Count > 0)
                {
                    int minCountPT = Message.Trips.Min(x => x.CountPT);
                    int maxCountPT = Message.Trips.Max(x => x.CountPT);
                    int diff = maxCountPT - minCountPT;
                    int countMinEl = Message.Trips.Count(x => ((x.CountPT >= minCountPT) && ((x.CountPT <= (minCountPT + 2)))));
                    int countMaxEl = Message.Trips.Count(x => (x.CountPT == maxCountPT));

                    if (diff >= 2)
                    {
                        if (countMinEl > 2)
                        {
                            for (int k = 0; k < Message.Trips.Count(); k++)
                            {
                                if (Message.Trips[k].CountPT >= (minCountPT + 2))
                                {
                                    Message.Trips[k].Info = "Too many PT (first check)";
                                    Message.TripsRemoved.Add(Message.Trips[k]);
                                    Message.Trips.Remove(Message.Trips[k]);
                                    k--;
                                }
                            }
                        }

                        /* In generale 5 o più mezzi sono eccessivi se la differenza tra il numero minimo e il numero massimo di PT è > 2 */
                        if ((minCountPT < 5) && (maxCountPT >= 5))
                        {
                            for (int k = 0; k < Message.Trips.Count(); k++)
                            {
                                if (Message.Trips[k].CountPT >= 5)
                                {
                                    Message.Trips[k].Info = "Too many PT (second check)";
                                    Message.TripsRemoved.Add(Message.Trips[k]);
                                    Message.Trips.Remove(Message.Trips[k]);
                                    k--;
                                }
                            }
                        }
                    }


                    /* Order the final list by Departure time */
                    Message.Trips = Message.Trips.OrderBy(x => int.Parse(x.Legs[0].DepartureTime)).ToList();

                    /* Order the final list by Global trip duration */
                    //Message.Trips = Message.Trips.OrderBy(x => x.TripDuration).ToList();
                }




                /* Suggest more walking connections if the destination is close to the PT (or carpooling) stop (in order to avoid to take further PTs for few stops) */
                if (Message.Trips.Count > 0)
                {
                    bool fullWalkingTripAlreadyAdded = false;
                    Contracts.Trip Trip = null;
                    int initialNumberOfTrips = Message.Trips.Count();

                    for (int k = 0; k < initialNumberOfTrips; k++)
                    {
                        Router X = null;
                        Contracts.Leg firstPTleg = null; //First PT or carpooling leg
                        Contracts.Leg lastPTleg = null; //Last PT or carpooling leg

                        /* Get the first PT (or carpooling) leg */
                        for (int m = 0; m < Message.Trips[k].Legs.Count; m++)
                        {
                            if (String.Compare(Message.Trips[k].Legs[m].Transport.TravelMode, "FEET") != 0)
                            {
                                firstPTleg = Message.Trips[k].Legs[m];
                                break;
                            }
                        }

                        /* Get the last PT (or carpooling) leg */
                        for (int m = (Message.Trips[k].Legs.Count - 1); m >= 0; m--)
                        {
                            if (String.Compare(Message.Trips[k].Legs[m].Transport.TravelMode, "FEET") != 0)
                            {
                                lastPTleg = Message.Trips[k].Legs[m];
                                break;
                            }
                        }

                        int chosenLegIndex = -1;
                        double distFromDest = -1;
                        double distCrowFliesFromLastPoint = -1;
                        double distCrowFliesFromStartingPoint = -1;
                        string origStartingTime = null;

                        /* Suggest walking alternative closest to the destination */
                        if (lastPTleg != null)
                        {
                            for (int m = 0; m < Message.Trips[k].Legs.Count; m++)
                            {
                                Trip = Message.Trips[k];

                                Point lastPoint = new Point(double.Parse(Message.Trips[k].Legs[m].Route.Points.Last().Coordinates.Latitude), double.Parse(Message.Trips[k].Legs[m].Route.Points.Last().Coordinates.Longitude));
                                distCrowFliesFromLastPoint = lastPoint.DistanceFrom(TargetNode.Point);
                                distCrowFliesFromStartingPoint = StartNode.Point.DistanceFrom(TargetNode.Point);

                                if (Trip.Legs[m].Transport.RouteID != lastPTleg.Transport.RouteID)
                                {
                                    if (distCrowFliesFromLastPoint < MAX_DIST_FROM_DEST)
                                    {
                                        double startingTime = double.Parse(Message.Trips[k].Legs[m].DepartureTime) + double.Parse(Message.Trips[k].Legs[m].Duration);
                                        ListNode = productionRoutingNetwork.ResolvePoint(lastPoint, delta);
                                        if (ListNode.Count() <= 0)
                                            break;
                                        RNode lastNode = ListNode.First();

                                        /* If we have only 1 pt for few stops, create a single walking leg by starting from the StartNode) */
                                        if ((Trip.CountPT == 1) || (distCrowFliesFromStartingPoint < MAX_DIST_FROM_DEST))
                                        {
                                            DateTime GlobalDate = new DateTime(int.Parse(DateString.Substring(0, 4)), int.Parse(DateString.Substring(4, 2)), int.Parse(DateString.Substring(6, 2)));
                                            startingTime = RoutePlanner.Globals.GetUnixTimeStamp(GlobalDate.AddSeconds(Time));
                                            /* Save the startingTime */
                                            origStartingTime = (Math.Round(startingTime)).ToString();
                                            X = new Router(StartNode, TargetNode, string.Empty, startingTime, TravelMode.Walking, ObjFunction.Distance);
                                        }
                                        else if (Trip.CountPT > 1)
                                            X = new Router(lastNode, TargetNode, string.Empty, startingTime, TravelMode.Walking, ObjFunction.Distance);
                                        else
                                            break;
                                        X.Start();
                                        distFromDest = X.RoutingPaths.First().Legs.First().Connections.Sum(x => x.GetDistance(TravelMode.Walking));

                                        if ((distFromDest < MAX_DIST_FROM_DEST))
                                        {
                                            chosenLegIndex = m;
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        //se lo step precedente è andato a buon fine
                        //duplica il trip
                        //eliminare tutti i leg successivo al leg corrente
                        //creare il nuovo leg a piedi partendo dalle informazioni presenti in X.RoutingPath.First()
                        ////calcolare il tempo di percorrenza
                        ////la distanza è già calcolata (distFromDest)
                        ////aggiungere etichette varie
                        //aggiungere il nuovo leg in calce alla lista dei leg
                        if ((X != null) && (chosenLegIndex != -1))
                        {
                            Contracts.Trip TripDup = new Contracts.Trip(Trip);

                            if ((Message.Trips[k].CountPT == 1) || (distCrowFliesFromStartingPoint < MAX_DIST_FROM_DEST))
                            {
                                if (!fullWalkingTripAlreadyAdded)
                                {
                                    /* Delete all legs (we have only 1 pt for few stops, create a single walking leg) */
                                    for (int n = Message.Trips[k].Legs.Count - 1; n >= 0; n--)
                                        Message.Trips[k].Legs.RemoveAt(n);

                                    fullWalkingTripAlreadyAdded = true;
                                }
                                else
                                    break;
                            }
                            else if (Message.Trips[k].CountPT > 1)
                            {
                                /* Delete next legs starting from the last one to the chosen one (excluded) */
                                for (int n = Message.Trips[k].Legs.Count - 1; n > chosenLegIndex; n--)
                                    Message.Trips[k].Legs.RemoveAt(n);
                            }
                            else
                                break;

                            /* Create the new walking leg */
                            SolutionLeg Leg = X.RoutingPaths[0].Legs[0];

                            if (Leg.Connections.Count > 0)
                            {
                                Contracts.Transport Transport = new Contracts.Transport();
                                Transport.TravelMode = "FEET";
                                string address = Leg.Connections.Last().GetTagValue("addr:housenumber") + "," + Leg.Connections.Last().GetTagValue("addr:street");
                                if (Leg.Connections.Last().GetTagValue("addr:street") == null)
                                    address = Leg.Connections.Last().GetTagValue("name");
                                Transport.LongName = address;
                                Transport.ShortName = address;
                                /* This condition is added to handle the case where all legs are deleted */
                                string departureTime = null;

                                if (Message.Trips[k].Legs.Count == 0)
                                    departureTime = origStartingTime;
                                else
                                    departureTime = Math.Round((int.Parse(Message.Trips[k].Legs[chosenLegIndex].DepartureTime) + double.Parse(Message.Trips[k].Legs[chosenLegIndex].Duration))).ToString();

                                Contracts.Route Route = new Contracts.Route();
                                for (int n = 0; n < Leg.Connections.Count; n++)
                                {
                                    Routing.Connections.Connection C = Leg.Connections[n];
                                    Contracts.Point Point = null;

                                    address = C.GetTagValue("addr:housenumber") + "," + C.GetTagValue("addr:street");
                                    if (C.GetTagValue("addr:street") == null)
                                        address = C.GetTagValue("name");

                                    Point = new Contracts.Point
                                    {
                                        Coordinates = new Contracts.Coordinates
                                        {
                                            Latitude = C.GetSource().Point.Latitude.ToString(),
                                            Longitude = C.GetSource().Point.Longitude.ToString(),
                                        },

                                        Date = departureTime,
                                        Address = address,
                                    };

                                    Route.Points.Add(Point);
                                }

                                string legDuration = "";
                                /* Used to fix the Infinity value coming from the algo for the walking leg */
                                if ((Leg.Duration == Double.PositiveInfinity) || (Leg.Duration == Double.NegativeInfinity))
                                    legDuration = (distFromDest / (float)1.4).ToString();
                                else
                                    legDuration = Leg.Duration.ToString();

                                /* Add the new walking leg */
                                Message.Trips[k].Legs.Add(new Contracts.Leg
                                {
                                    Transport = Transport,
                                    DepartureTime = departureTime,
                                    Distance = distFromDest.ToString(),
                                    Duration = legDuration,
                                    Route = Route,
                                });

                                Message.Trips.Add(TripDup);
                            }
                        }
                    }

                    /* Order the final list by Departure time */
                    Message.Trips = Message.Trips.OrderBy(x => int.Parse(x.Legs[0].DepartureTime)).ToList();

                    /* Order the final list by Global trip duration */
                    Message.Trips = Message.Trips.OrderBy(x => x.TripDuration).ToList();
                }


                /* Remove trips where a PT is taken twice or more */
                for (int k = 0; k < Message.Trips.Count; k++)
                {
                    List<string> listPT = new List<string> { };

                    for (int m = 0; m < Message.Trips[k].Legs.Count(); m++)
                    {
                        if ((Message.Trips[k].Legs[m].Transport.TravelMode != "FEET") && (Message.Trips[k].Legs[m].Transport.TravelMode != "CAR_POOLING"))
                        {
                            if (listPT.Contains(Message.Trips[k].Legs[m].Transport.LongName))
                            {
                                Message.Trips[k].Info = "PT " + Message.Trips[k].Legs[m].Transport.LongName + " is proposed twice on the same trip";
                                Message.TripsRemoved.Add(Message.Trips[k]);
                                Message.Trips.Remove(Message.Trips[k]);
                                k--;
                                break;
                            }
                            else
                            {
                                listPT.Add(Message.Trips[k].Legs[m].Transport.LongName);
                            }
                        }
                    }

                    //Re-sorting the solution list is not needed
                }

                /* Remove trips if they starts more than 3 hours after starting time set by the user */
                for (int k = 0; k < Message.Trips.Count; k++)
                {
                    DateTime GlobalDate = new DateTime(int.Parse(DateString.Substring(0, 4)), int.Parse(DateString.Substring(4, 2)), int.Parse(DateString.Substring(6, 2)));
                    double userDepartureTime = RoutePlanner.Globals.GetUnixTimeStamp(GlobalDate.AddSeconds(Time));

                    if (double.Parse(Message.Trips[k].Legs.First().DepartureTime) > (userDepartureTime + THRESHOLD_FUTURE_TRIPS))
                    {
                        Message.Trips[k].Info = "PT will start too late " + Message.Trips[k].Legs.First().DepartureTime;
                        Message.TripsRemoved.Add(Message.Trips[k]);
                        Message.Trips.Remove(Message.Trips[k]);
                        k--;
                    }
                    //Re-sorting the solution list is not needed
                }

                return Message;
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                Message.Result              = false;
                Message.Error.Code          = Message.Error.GetDefaultValueAttributeFromEnumValue(ErrorCodes.GENERAL_ERROR);
                Message.Error.Message       = ex.ToString();

                return Message;
            }

        }
    }
}
