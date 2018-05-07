using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Configuration;
using System.IO;
using SocialCar.RoutePlanner.Routing;
using SocialCar.RoutePlanner.Routing.Nodes;
using SocialCar.RoutePlanner.Routing.Connections;
using SocialCar.RoutePlanner.MapMatching;
using log4net;
using log4net.Config;
using Newtonsoft.Json;
using System.Net;
using System.Runtime.Serialization.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace SocialCar.RoutePlanner.Carpools
{
    public class CarpoolParser
    {
        public List<Carpooler> CarPoolers = new List<Carpooler>();

        private RoutingNetwork RoadNetwork;

        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public static int CARPOOLING_MAX_DISTANCE_PTCONNECTIONS = 2000;

        public CarpoolParser(RoutingNetwork network, List<Carpooler> CarPoolers)
        {
            RoadNetwork = network;
            this.CarPoolers = CarPoolers;
        }

        public void removeCarPooler(Carpooler el)
        {
            CarPoolers.Remove(el);
        }

        public void BuildCarpoolRoutesFromXML()
        {
            string file = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory + ConfigurationManager.AppSettings["dataPath"] + ConfigurationManager.AppSettings["CarpoolXMLFile"]);
            log.Info(file);

            if (System.IO.File.Exists(file))
            {
                XDocument document = XDocument.Load(file);
                XElement root = document.FirstNode as XElement;
                IEnumerable<XElement> Routes = root.Elements("route");
                ///
                foreach (XElement route in Routes)
                {
                    Carpooler Pooler = new Carpooler(
                        route.Element("pooler").Attribute("id").Value,
                        route.Element("pooler").Attribute("name").Value,
                        Globals.ConvertTimeToSeconds(route.Element("pooler").Attribute("starttime").Value), 
                        int.Parse(route.Element("pooler").Attribute("capacity").Value), true, "", SourceCPRide.Unknown);

                    CarPoolers.Add(Pooler);
                    
                    IEnumerable<XElement> wayPoints = route.Elements("wayPoint");
                    foreach (XElement wayPoint in wayPoints)
                    {
                        Pooler.WayPointsOrig.Add( new Point(double.Parse(wayPoint.Attribute("lat").Value), double.Parse(wayPoint.Attribute("lng").Value)) );
                        Pooler.WayPointsUsed.Add( new Point(double.Parse(wayPoint.Attribute("lat").Value), double.Parse(wayPoint.Attribute("lng").Value)) );
                    }
                }
            }
        }

        //public void BuildCarpoolRoutesFromJson()
        //{
        //    string file = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory + ConfigurationManager.AppSettings["dataPath"] + ConfigurationManager.AppSettings["CarpoolJSONFile"]);
        //    log.Info(file);

        //    if (System.IO.File.Exists(file))
        //    {
        //        List<CarpoolerRdex> CarpoolerList = new List<CarpoolerRdex> { };
        //        CarpoolerList = JsonConvert.DeserializeObject<List<CarpoolerRdex>>(File.ReadAllText(file));

        //        foreach (CarpoolerRdex el in CarpoolerList)
        //        {
        //            Carpooler Pooler = new Carpooler(el.uuid, Globals.ConvertTimeToSeconds(el.outward.mintime.ToString()), el.driver.seats);

        //            CarPoolers.Add(Pooler);

        //            /* Split wayPoints */
        //            string[] wayPoints = el.waypoints.Split(';');

        //            foreach (string wp in wayPoints)
        //            {
        //                string[] coordinate = wp.Split(',');
        //                Pooler.WayPoints.Add( new Point(double.Parse(coordinate[0]), double.Parse(coordinate[1])) );
        //            }

        //        }
        //    }
        //}

        public void BuildCarpoolRoutesFromCSV()
        {
            string file = @"stop_times.txt";
            if (System.IO.File.Exists(file))
            {
                using (StreamReader sr = new StreamReader(file))
                {
                    Carpooler Pooler = null;

                    string currentLine;
                    bool Header = false;
                    string id = "";
                    string name = "";

                    while ((currentLine = sr.ReadLine()) != null)
                    {
                        if (!Header)
                        {
                            Header = true;
                            continue;
                        }

                        string[] fields = currentLine.Split(',');

                        if (String.Compare(id, fields[0]) != 0)
                        {
                            id = fields[0];
                            Pooler = new Carpooler(id, name, Globals.ConvertTimeToSeconds(fields[1]), 10, true, "", SourceCPRide.Unknown);
                            CarPoolers.Add(Pooler);
                        }

                        double Lat = double.Parse(fields[3].Split('_')[0]);
                        double Lng = double.Parse(fields[3].Split('_')[1]);

                        Pooler.WayPointsOrig.Add(new Point(Lat, Lng));
                        Pooler.WayPointsUsed.Add(new Point(Lat, Lng));
                    }
                }
            }
        }

        public List<RNode> BuildCarpoolRoutesFromPoints(List<Point> Points)
        {
            HiddenMarkovModelMapMatcher MapMatcher = new HiddenMarkovModelMapMatcher(ref RoadNetwork, Points);
            return MapMatcher.Match();
        }

        public void ConnectWithRoadNetwork()
        {
            /* Set this to true if you want to use real waypoints instead of intermediate ptstops, remember to also change the use_waypoints value in the HTTPRequests.cs file */
            bool use_waypoints = false;

            int i = 1;
            float delta = 0.1f;

            foreach (Carpooler P in CarPoolers)
            {
                log.Info(i + "/" + CarPoolers.Count + " Adding carpooling ride id:" + P.Id + " Name:\"" + P.Name + "\" Provider:" + P.Provider + 
                    " Departure:" + P.WayPointsOrig.First().Latitude + "," + P.WayPointsOrig.First().Longitude + 
                    " Destination:" + P.WayPointsOrig.Last().Latitude + "," + P.WayPointsOrig.Last().Longitude + 
                    " TripDate:" + P.TripDate + " TripStartTime:" + P.TripStartTime + " to the RoadNetwork");

                /* Find the closest PTstop inside the map along the carpooling ride (but only if the Carpooling ride starting point is outside the map) */
                //Commented because actually we are ONLY considering carpooling rides included into the map
                //setNewDeparturePoint(P, delta, i);

                /* Set the closest PT stops to the departure and to the destination points */
                /* Route planner does not use waypoints, it uses the PT stops */
                if (use_waypoints == false)
                    setTheClosestPTstops(P, i);


                HiddenMarkovModelMapMatcher MapMatcher = new HiddenMarkovModelMapMatcher(ref RoadNetwork, P.WayPointsUsed);


                if (use_waypoints == true)
                {
                    /* Route planner uses waypoints */
                    List<RNode> Nodes = MapMatcher.Match();
                    for (int j = 0; j < Nodes.Count; j++)
                        log.Info("Stops: " + (j + 1) + "/" + Nodes.Count + " - " + Nodes[j].Point.Latitude + "," + Nodes[j].Point.Longitude);
                    ConnectNodesWithRoadNetwork(P, Nodes);
                }
                else
                {
                    /* Route planner does not use waypoints, it uses the PT stops */
                    List<TNodeCarpooling> Nodes = MapMatcher.MatchTNodeCarpooling();
                    for (int j=0; j<Nodes.Count; j++)
                        log.Info("Stops: " + (j+1) + "/" + Nodes.Count + " \"" + Nodes[j].StopName + "\" - " + Nodes[j].Point.Latitude + "," + Nodes[j].Point.Longitude);
                    ConnectNodesWithRoadNetworkTNode(P, Nodes);
                }


                RoadNetwork.AddCarpooler(P);
                i++;
            }

            if (use_waypoints == true)
                RoadNetwork.ConnectCarpools();
            else
                RoadNetwork.ConnectCarpoolsTNode();
        }

        /* 
         * Find the closest PTstop to the departure and destination points
         */
        public void setTheClosestPTstops(Carpooler P, int i)
        {
            /* Find the closest PTstop to the departure point */
            List<TNodeCarpooling> PTstops = RoadNetwork.findClosestStop(P.WayPointsOrig.First());
            P.WayPointsUsed.Remove(P.WayPointsUsed.First());
            P.WayPointsUsed.Insert(0, PTstops.First().Point);
            log.Info(i + "/" + CarPoolers.Count + " orig starting point: " + P.WayPointsOrig.First().Latitude + "," + P.WayPointsOrig.First().Longitude +
                " new starting point:" + P.WayPointsUsed.First().Latitude + "," + P.WayPointsUsed.First().Longitude + " stopName: " + PTstops.First().StopName);

            /* Find the closest PTstop to the destination point */
            PTstops = RoadNetwork.findClosestStop(P.WayPointsOrig.Last());
            P.WayPointsUsed.Remove(P.WayPointsUsed.Last());
            P.WayPointsUsed.Add(PTstops.First().Point);
            log.Info(i + "/" + CarPoolers.Count + " orig destination point: " + P.WayPointsOrig.Last().Latitude + "," + P.WayPointsOrig.Last().Longitude +
                " new destination point:" + P.WayPointsUsed.Last().Latitude + "," + P.WayPointsUsed.Last().Longitude + " stopName: " + PTstops.First().StopName);
        }

        /* 
         * Find the closest PTstop inside the map along the carpooling ride (but only if the Carpooling ride starting point is outside the map) 
         */
        public void setNewDeparturePoint(Carpooler P, float delta, int i)
        {
            List<RNode> ListNodeP = RoadNetwork.ResolvePoint(P.WayPointsOrig.First(), delta);

            if (ListNodeP.Count() == 0)
            {
                int j = 0;
                /* Track a line from starting point to the end point and intersect the map rectangle in one point, then try to search the nearest PT stop to this point */
                /* Find the intersection point between the line from source to destination and the map boundaries */
                Routing.Nodes.Point newStartingPoint;
                bool doit = true;
                doit = checkIntersection(
                    P.WayPointsOrig.First().Longitude,
                    P.WayPointsOrig.First().Latitude,
                    P.WayPointsOrig.Last().Longitude,
                    P.WayPointsOrig.Last().Latitude,
                    RoadNetwork.MinPoint.Longitude,
                    RoadNetwork.MinPoint.Latitude,
                    RoadNetwork.MaxPoint.Longitude,
                    RoadNetwork.MaxPoint.Latitude,
                    out newStartingPoint
                    );

                if (doit)
                {
                    /* Find the closest stop to the intersection point */
                    List<TNodeCarpooling> PTstops = RoadNetwork.findClosestStop(newStartingPoint);

                    List<RNode> ListNode = new List<RNode> { };
                    for (j = 0; j < PTstops.Count; j++)
                    {
                        ListNode = RoadNetwork.ResolvePoint(PTstops[j].Point, delta);

                        if (ListNode.Count() > 0)
                        {
                            P.WayPointsUsed.Remove(P.WayPointsUsed.First());
                            P.WayPointsUsed.Insert(0, PTstops[j].Point);
                            log.Info(i + "/" + CarPoolers.Count + " orig starting point: " + P.WayPointsOrig.First().Latitude + "," + P.WayPointsOrig.First().Longitude +
                                " new starting point:" + P.WayPointsUsed.First().Latitude + "," + P.WayPointsUsed.First().Longitude + " stopName: " + PTstops[j].StopName);
                            break;
                        }
                    }
                }
                else
                {
                    log.Warn("checkIntersection returned false");
                }
            }
        }


        /*
         * Check where the line from the departure point to the destination point intersects the map boundaries
         * These new coordinates will be used to find the closest PT stop. This stop will be the new starting point of the ride.
         */
        public bool checkIntersection(double x1, double y1, double x2, double y2, double minX, double minY, double maxX, double maxY, out Point newStartingPoint)
        {

            newStartingPoint = null;

            // Completely outside.
            if ((x1 <= minX && x2 <= minX) || (y1 <= minY && y2 <= minY) || (x1 >= maxX && x2 >= maxX) || (y1 >= maxY && y2 >= maxY))
                return false;

            double m = (y2 - y1) / (x2 - x1);
            double q = y1 - (m * x1);

            double x = (minY - q) / m;
            if ( (x > minX && x < maxX) && (minY > y1 && minY < y2) )
            {
                //From South
                newStartingPoint = new Routing.Nodes.Point(minY, x);
                return true;
            }

            x = (maxY - q) / m;
            if ( (x > minX && x < maxX) && (maxY > y2 && maxY < y1) )
            {
                //From North
                newStartingPoint = new Routing.Nodes.Point(maxY, x);
                return true;
            }

            double y = (m * minX) + q;
            if ( (y > minY && y < maxY) && (minX > x1 && minX < x2) )
            {
                //From East
                newStartingPoint = new Routing.Nodes.Point(y, minX);
                return true;
            }

            y = (m * maxX) + q;
            if ((y > minY && y < maxY) && (maxX > x2 && maxX < x1))
            {
                //From West
                newStartingPoint = new Routing.Nodes.Point(y, maxX);
                return true;
            }
            
            return false;
        }
        

        private void ConnectNodesWithRoadNetwork(Carpooler Pooler, List<RNode> Nodes)
        {
            Nodes = Nodes.Where(x => x != null).ToList();
            CNode src = null, dst = null;

            for (int i = 0 ; i < Nodes.Count - 1; ++i)
            {
                src = RoadNetwork.GetNode(Nodes[i].Point);
                dst = RoadNetwork.GetNode(Nodes[i + 1].Point);

                if (i == 0)
                    Pooler.SetSource(src);

                Dictionary<string, string> tags = this.RoadNetwork.GetConnectionTags(src, dst);

                int srcArrivalTime = Pooler.GetLastArrivalTime();
                int travelTime = (int)RoadNetwork.AreConnected(Nodes[i], Nodes[i + 1]).GetTravelTime(string.Empty, Pooler.GetLastArrivalTime(), TravelMode.Car);
                int dstArrivalTime = srcArrivalTime + travelTime;

                CConnection C = RoadNetwork.AddConnection(src, dst, srcArrivalTime, dstArrivalTime, ref Pooler, tags);
                
                Pooler.AddConnection(ref C);

                Pooler.AddNextArrivalTime(dstArrivalTime);
            }
        }

        /*TEST********************************************************************************************************************************/
        /*
         * 
         */ 
        private void ConnectNodesWithRoadNetworkTNode(Carpooler Pooler, List<TNodeCarpooling> Nodes)
        {
            Nodes = Nodes.Where(x => x != null).ToList();
            CNode src = null, dst = null;
            int totDuration = 0;

            //long elapsed_time_GetNode = 0;
            //long elapsed_time_Other = 0;
            //long elapsed_time_AddConnection = 0;
            //long elapsed_time_AddConnection2 = 0;

            for (int i = 0; i < Nodes.Count - 1; ++i)
            {
                //var stopwatch_GetNode = new Stopwatch();
                //stopwatch_GetNode.Start();
                src = RoadNetwork.GetNode(Nodes[i].Point, Nodes[i].StopName);
                dst = RoadNetwork.GetNode(Nodes[i + 1].Point, Nodes[i + 1].StopName);
                //stopwatch_GetNode.Stop();
                //elapsed_time_GetNode += stopwatch_GetNode.ElapsedMilliseconds;


                if (i == 0)
                    Pooler.SetSource(src);

                Dictionary<string, string> tags = new Dictionary<string, string>(); // this.RoadNetwork.GetConnectionTags(src, dst);

                /* BAD: the conversion is already done in HTTPRequest.cs getCarpoolingDataFromBackend*/
                /* Convert time from seconds to HH:MM:SS */
                //TimeSpan time = TimeSpan.FromSeconds(Pooler.TripStartTime);
                //DateTime dateTime = DateTime.Today.Add(time);
                //string str = dateTime.ToString("hh:mm:ss");
                //DateTime srcArrivalDateTime = Globals.ConvertDateAndTimeToLocalSiteDateTime(Pooler.TripDate, str);
                //Seconds since midnight of the http request
                //int srcArrivalTime = (srcArrivalDateTime.Hour * 3600) + (srcArrivalDateTime.Minute * 60) + srcArrivalDateTime.Second;

                int srcArrivalTime = Pooler.GetLastArrivalTime();
                
                //var stopwatch_Other = new Stopwatch();
                //stopwatch_Other.Start();
                //double travelTime = RoadNetwork.AreConnected(Nodes[i], Nodes[i + 1]).GetTravelTime(string.Empty, Pooler.GetLastArrivalTime(), TravelMode.Car);
                int travelTime = (int)RoadNetwork.AreConnected(Nodes[i], Nodes[i + 1], "carpoolingride").GetTravelTime(string.Empty, Pooler.GetLastArrivalTime(), TravelMode.Carpool);
                //stopwatch_Other.Stop();
                //elapsed_time_Other += stopwatch_Other.ElapsedMilliseconds;
                int dstArrivalTime = srcArrivalTime + travelTime;



                //var stopwatch_AddConnection = new Stopwatch();
                //stopwatch_AddConnection.Start();
                CConnection C = RoadNetwork.AddConnection(src, dst, srcArrivalTime, dstArrivalTime, ref Pooler, tags);
                int duration = C.DstArrivalTime - C.DepartureTime;
                log.Info("Connection " + (i + 1) + "/" + Nodes.Count + " - Src:\"" + C.Source.StopName + "\" - Dst:\"" + C.Destination.StopName + "\" - Duration:" + duration);
                //stopwatch_AddConnection.Stop();
                //elapsed_time_AddConnection += stopwatch_AddConnection.ElapsedMilliseconds;

                //var stopwatch_AddConnection2 = new Stopwatch();
                //stopwatch_AddConnection2.Start();
                Pooler.AddConnection(ref C);
                Pooler.AddNextArrivalTime(dstArrivalTime);
                //stopwatch_AddConnection2.Stop();
                //elapsed_time_AddConnection2 += stopwatch_AddConnection2.ElapsedMilliseconds;

                totDuration += duration; 
            }

            log.Info("Total carpooling trip duration: " + totDuration);

            //log.Info("elapsed_time_GetNode:" + elapsed_time_GetNode);
            //log.Info("elapsed_time_Other:" + elapsed_time_Other);
            //log.Info("elapsed_time_AddConnection:" + elapsed_time_AddConnection);
            //log.Info("elapsed_time_AddConnection2:" + elapsed_time_AddConnection2);
        }
        /********************************************************************************************************************************/

        public static CarpoolDiff checkDifferences(List<Carpooler> oldList, List<Carpooler> newList, bool updateExternalRides)
        {
            CarpoolDiff Diff = new CarpoolDiff { };

            foreach (Carpooler elNew in newList)
            {
                if ((elNew.Provider == SourceCPRide.SocialCar) ||
                    ((elNew.Provider == SourceCPRide.External) && (updateExternalRides == true)))
                {
                    int index = -1;
                    index = oldList.FindIndex(x => x.Id == elNew.Id);

                    /* Check if the element is exactly the same, in case ignore it and remove from the old list */
                    if (index != -1)
                    {
                        /* Check if the waypoints are the same */
                        bool equalWaypoints = true;
                        if (oldList[index].WayPointsOrig.Count == elNew.WayPointsOrig.Count)
                        {
                            for (int j = 0; j < oldList[index].WayPointsOrig.Count; j++)
                            {
                                if ((oldList[index].WayPointsOrig[j].Latitude != elNew.WayPointsOrig[j].Latitude) ||
                                    (oldList[index].WayPointsOrig[j].Longitude != elNew.WayPointsOrig[j].Longitude))
                                {
                                    equalWaypoints = false;
                                    break;
                                }
                            }
                        }

                        if ( (oldList[index].TripStartTime == elNew.TripStartTime) &&
                             (oldList[index].TripDate == elNew.TripDate) &&
                             (oldList[index].Capacity == elNew.Capacity) &&
                             (oldList[index].WayPointsOrig.Count == elNew.WayPointsOrig.Count) &&
                             (equalWaypoints == true) )
                        {
                            /* The carpooler ride already exists and it is not changed (nothing to do in the network, we can add it to the ignore list) */
                            Diff.ElementsToIgnore.Add(elNew);
                        }
                        else
                        {
                            /* The carpooler ride already exists but it is changed (so we need to remove it from the network and add the new one) */
                            Diff.ElementsToAdd.Add(elNew);
                            Diff.ElementsToRemove.Add(oldList[index]);
                        }
                    }
                    else
                    {
                        /* The carpooler ride does not exist (so we need to add it to the network) */
                        Diff.ElementsToAdd.Add(elNew);
                    }
                }
            }


            /* Add all the elements not included in "Added" or "Removed" into the "Removed" list */
            foreach (Carpooler elOld in oldList)
            {
                if ((elOld.Provider == SourceCPRide.SocialCar) ||
                    ((elOld.Provider == SourceCPRide.External) && (updateExternalRides == true)))
                {
                    int index = -1;
                    index = Diff.ElementsToAdd.FindIndex(x => x.Id == elOld.Id);
                    if (index == -1)
                        index = Diff.ElementsToRemove.FindIndex(x => x.Id == elOld.Id);
                    if (index == -1)
                        index = Diff.ElementsToIgnore.FindIndex(x => x.Id == elOld.Id);

                    if (index == -1)
                        Diff.ElementsToRemove.Add(elOld);
                }
            }

            /* SUMMARY */
            log.Info("updateExternalRides: " + updateExternalRides);
            log.Info("oldList: " + oldList.Count + " tot elements");
            log.Info("newList: " + newList.Count + " tot elements");
            log.Info("Diff.ElementsToAdd: " + Diff.ElementsToAdd.Count);
            log.Info("Diff.ElementsToRemove: " + Diff.ElementsToRemove.Count);
            log.Info("Diff.ElementsToIgnore: " + Diff.ElementsToIgnore.Count);

            return Diff;
        }

    }

}
