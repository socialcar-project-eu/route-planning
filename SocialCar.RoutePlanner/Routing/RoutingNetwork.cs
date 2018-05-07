using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Configuration;
using SocialCar.RoutePlanner.Routing.Nodes;
using SocialCar.RoutePlanner.Routing.Connections;
using SocialCar.RoutePlanner.Containers;
using SocialCar.RoutePlanner.Carpools;
using log4net;
using log4net.Config;
using System.Device.Location;

namespace SocialCar.RoutePlanner.Routing
{
    [Serializable]
    public class RoutingNetwork
    {
        private Dictionary<long, Node> GNodes;
        private Dictionary<long, Connection> Connections;

        private Dictionary<long, RNode> RNodes;
        private Dictionary<long, RConnection> RConnections;

        private Dictionary<long, TNode> TNodes;
        private Dictionary<long, TConnection> TConnections;

        private Dictionary<long, TNodeCarpooling> TNodesCarpooling;
        private Dictionary<long, TConnectionForCarpooling> TConnectionsForCarpoolingRides;

        private Dictionary<long, CNode> CNodes;
        private Dictionary<long, CConnection> CConnections;

        public List<Carpools.Carpooler> Carpoolers { get; private set; }
        public int CarpoolingMaxConnectionsPerTNode;

        public List<Traffic.TrafficReport> TrafficReport { get; private set; }
        public double TrafficPropagationMaxDistance; //meters

        //private QuadTree<QuadTreeNodeItem<RNode>> SpatialQuadTree;
        private Containers.RTree.RTree<RNode> SpatialQuadTree;

        public Point MinPoint { get; private set; }
        public Point MaxPoint { get; private set; }

        private long __MaxArcID = 1;

        /* This variable is used to kkep track of the number of existing carpooling rides deleted from the network (in order to let the "__MaxArcID" logic works) */
        private long __NumCCDeleted = 0;

        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        //public List<Agency> AgencyTable { get; private set; }

        //public class Agency
        //{
        //    public string name;
        //    public string id;
        //}

        //public void addAgency(string id, string name)
        //{
        //    Agency a = new Agency();
        //    a.id = id;
        //    a.name = name;
        //    AgencyTable.Add(a);
        //}

        public RoutingNetwork()
        {
            this.RNodes = new Dictionary<long, RNode>();
            this.TNodes = new Dictionary<long, TNode>();
            this.TNodesCarpooling = new Dictionary<long, TNodeCarpooling>();
            this.GNodes = new Dictionary<long, Node>();
            this.CNodes = new Dictionary<long, CNode>();

            this.Connections = new Dictionary<long, Connection>();
            this.RConnections = new Dictionary<long, RConnection>();
            this.TConnections = new Dictionary<long, TConnection>();
            this.TConnectionsForCarpoolingRides = new Dictionary<long, TConnectionForCarpooling>();
            this.CConnections = new Dictionary<long, CConnection>();

            this.Carpoolers = new List<Carpools.Carpooler>();
            this.TrafficReport = new List<Traffic.TrafficReport>();

            //this.AgencyTable = new List<Agency>();
        }

        public void AddCarpooler(Carpools.Carpooler Pooler)
        {
            this.Carpoolers.Add(Pooler);
        }

        public void RemoveCarpooler(Carpools.Carpooler Pooler)
        {
            this.Carpoolers.Remove(Pooler);
        }

        public void SetBoundaries(Point MinPoint, Point MaxPoint)
        {
            this.MinPoint = MinPoint;
            this.MaxPoint = MaxPoint;

            //this.SpatialQuadTree = new QuadTree<QuadTreeNodeItem<RNode>>(
            //    new System.Drawing.RectangleF(
            //            (float)MinPoint.Longitude, (float)MinPoint.Latitude,
            //            (float)(MaxPoint.Longitude - MinPoint.Longitude + 0.1f),
            //            (float)(MaxPoint.Latitude - MaxPoint.Latitude + 0.1f)
            //            )
            //        );

            this.SpatialQuadTree = new Containers.RTree.RTree<RNode>();
            Containers.RTree.Rectangle rect = new Containers.RTree.Rectangle((float)MinPoint.Longitude, (float)MinPoint.Latitude,
                (float)MaxPoint.Longitude, (float)MaxPoint.Latitude, 
                0, 0);
        }

        public void ConnectTransportation()
        {
            RNode n = null;
            List<RNode> nList = null;
            foreach (TNode t in TNodes.Values)
            {
                nList = ResolvePoint(t.Point, 0.01f);
                if (nList.Count > 0)
                {
                    n = nList.First();
                    AddConnection(t, n);
                    AddConnection(n, t);
                }
                else
                {
                    log.Warn("Transportation void node list (nList.Count: " + nList.Count + " t.Id:" + t.Id + " t.Point.Latitude:" + t.Point.Latitude + " t.Point.Longitude:" + t.Point.Longitude + ")");
                }
            }
        }

        public void ConnectCarpools()
        {
            RNode n = null;
            List<RNode> nList = null;
            foreach (CNode c in CNodes.Values)
            {
                nList = ResolvePoint(c.Point, 0.01f);
                if (nList.Count > 0)
                {
                    n = nList.First();
                    AddConnection(c, n);
                    AddConnection(n, c);
                }
                else
                {
                    log.Warn("Carpools void node list (nList.Count: " + nList.Count + " c.Id:" + c.Id + " c.Point.Latitude:" + c.Point.Latitude + " c.Point.Longitude:" + c.Point.Longitude + ")");                }
            }
        }

        public void ConnectCarpoolsTNode()
        {
            TNode n = null;
            List<TNodeCarpooling> nList = null;
            foreach (CNode c in CNodes.Values)
            {
                nList = ResolvePointTNode(c.Point, 0.01f);
                if (nList.Count > 0)
                {
                    n = nList.First();
                    AddConnection(c, n);
                    AddConnection(n, c);
                }
                else
                {
                    log.Warn("Carpools void node list (nList.Count: " + nList.Count + " c.Id:" + c.Id + " c.Point.Latitude:" + c.Point.Latitude + " c.Point.Longitude:" + c.Point.Longitude + ")");
                }
            }
        }

        /*
         * Returns the closest point on the map based on the TConnection network created for carpooling rides
         */
        public List<TNodeCarpooling> ResolvePointTNode(Point P, float delta, int n = 1)
        {
            double distance;

            SortedDictionary<double, List<TNodeCarpooling>> Q = new SortedDictionary<double, List<TNodeCarpooling>>();

            Containers.RTree.Rectangle rect = new Containers.RTree.Rectangle((float)P.Longitude - delta / 2.0f, (float)P.Latitude - delta / 2.0f,
                (float)P.Longitude + delta / 2.0f, (float)P.Latitude + delta / 2.0f, 0, 0);

            List<RNode> Nodes = SpatialQuadTree.Contains(rect);

            foreach (KeyValuePair<long, TNodeCarpooling> TNC in TNodesCarpooling)
            {
                distance = TNC.Value.Point.DistanceFrom(P);
                if (distance < CarpoolParser.CARPOOLING_MAX_DISTANCE_PTCONNECTIONS)
                {
                    if (Q.ContainsKey(distance))
                        Q[distance].Add(TNC.Value);
                    else
                    {
                        Q.Add(distance, new List<TNodeCarpooling>());
                        Q[distance].Add(TNC.Value);
                    }
                }
            }

            return Q.SelectMany(x => x.Value).Take(n).ToList();

        }

        /*
         * Returns the closest point on the map
         */
        public List<RNode> ResolvePoint(Point P, float delta, int n = 1)
        {
            double distance;

            SortedDictionary<double, List<RNode>> Q = new SortedDictionary<double, List<RNode>>();

            Containers.RTree.Rectangle rect = new Containers.RTree.Rectangle((float)P.Longitude - delta / 2.0f, (float)P.Latitude - delta / 2.0f,
                (float)P.Longitude + delta / 2.0f, (float)P.Latitude + delta / 2.0f, 0, 0);

            List<RNode> Nodes = SpatialQuadTree.Contains(rect);

            foreach (RNode N in Nodes)
            {
                distance = N.Point.DistanceFrom(P);
                if (Q.ContainsKey(distance))
                    Q[distance].Add(N);
                else
                {
                    Q.Add(distance, new List<RNode>());
                    Q[distance].Add(N);
                }
            }

            return Q.SelectMany(x => x.Value).Take(n).ToList();

        }

        public RNode AddNode(long id, Point Coordinates)
        {
            RNode N = new RNode(id, Coordinates);
            GNodes.Add(id, N);
            RNodes.Add(id, N);
            //SpatialQuadTree.Insert(new QuadTreeNodeItem<RNode>(N, (float)N.Point.Longitude, (float)N.Point.Latitude));

            Containers.RTree.Rectangle rect = new Containers.RTree.Rectangle((float)Coordinates.Longitude, (float)Coordinates.Latitude, 
                (float)Coordinates.Longitude + 0.001f, (float)Coordinates.Latitude + 0.001f, 0, 0);

            SpatialQuadTree.Add(rect, N);

            return N;
        }

        public TNode AddNode(long id, Point Coordinates, string StopId, string StopName)
        {
            TNode N = new TNode(id, Coordinates)
            {
                StopId = StopId,
                StopName = StopName
            };

            GNodes.Add(id, N);
            TNodes.Add(id, N);

            return N;
        }

        public TNodeCarpooling AddNodeCarpooling(long id, Point Coordinates, string StopId, string StopName)
        {
            TNodeCarpooling N = new TNodeCarpooling(id, Coordinates, StopId, "", StopName, 0);
            TNodesCarpooling.Add(id, N);

            return N;
        }

        public Node GetNode(long id)
        {
            if (GNodes.ContainsKey(id))
                return GNodes[id];

            throw new Exception("Node not found");
        }

        public CNode GetNode(Point P, string stopName="")
        {
            if (CNodes == null)
            {
                this.CNodes = new Dictionary<long, CNode>();
                this.CConnections = new Dictionary<long, CConnection>();
            }

            long id = CNodes.Count;
            if (id!=0)
                id = CNodes.Aggregate((l, r) => l.Key > r.Key ? l : r).Key;

            var c = CNodes.Where(x => x.Value.Point == P).FirstOrDefault().Value;

            if (c == null)
            {
                var n = new CNode(id + 1, P, stopName);
                CNodes.Add(n.Id, n);
                return n;
            }
            else
                return c;
        }

        public Connection GetConnection(long id)
        {
            if (Connections.ContainsKey(id))
                return Connections[id];

            throw new Exception("Connection not found");
        }

        public RConnection AddConnection(RNode src, RNode dst, bool isOneWay, bool isOnlyFoot)
        {
            RConnection C = new RConnection(__MaxArcID, src, dst, isOneWay, isOnlyFoot);
            src.Connections.Add(C);
            Connections.Add(__MaxArcID, C);
            RConnections.Add(__MaxArcID, C);
            ++(__MaxArcID);
            return C;
        }

        public RConnection AreConnected(RNode src, RNode dst)
        {
            //This returns null values (NULL => CRASH) if the carpooling waypoints are enabled
            return RConnections.Values.Where(x => x.GetSource().Id == src.Id &&
                x.GetDestination().Id == dst.Id).FirstOrDefault(); ;
        }

        public TConnection AddConnection(TNode src, TNode dst)
        {
            TConnection C = new TConnection(__MaxArcID, src, dst);
            src.Connections.Add(C);
            Connections.Add(__MaxArcID, C);
            TConnections.Add(__MaxArcID, C);
            ++(__MaxArcID);
            return C;
        }

        public TConnection AreConnected(TNode src, TNode dst, string routeID)
        {
            return TConnections.Values.Where(x => x.GetSource().Id == src.Id && 
                x.GetDestination().Id == dst.Id).Where(x => x.TimeTable.RouteId == routeID).FirstOrDefault();
        }


        /* 
         * Add connection for the carpooling network based on PT stops
         */
        public TConnectionForCarpooling AddConnectionForCarpoolingRides(TNodeCarpooling src, TNodeCarpooling dst)
        {
            TConnectionForCarpooling C = new TConnectionForCarpooling(__MaxArcID, src, dst);
            src.Connections.Add(C);
            Connections.Add(__MaxArcID, C);
            TConnectionsForCarpoolingRides.Add(__MaxArcID, C);
            ++(__MaxArcID);
            return C;
        }

        public TConnectionForCarpooling AreConnected(TNodeCarpooling src, TNodeCarpooling dst, string routeID)
        {
            TConnectionForCarpooling TC = TConnectionsForCarpoolingRides.Values.Where(x => x.GetSource().Id == src.Id && x.GetDestination().Id == dst.Id).FirstOrDefault();

            if (TC == null)
            {
                double distanceFromSource = src.Point.DistanceFrom(dst.Point);
                this.AddConnectionForCarpoolingRides(src, dst);
                TC = TConnectionsForCarpoolingRides.Values.Where(x => x.GetSource().Id == src.Id && x.GetDestination().Id == dst.Id).FirstOrDefault();
                log.Warn("Not connected, added connection from \"" + src.StopName + "\" to \"" + dst.StopName + "\" (distance: " + distanceFromSource + "m)");
            }

            return TC;
        }

        public LConnection AddConnection(RNode src, RNode dst)
        {
            LConnection C = new LConnection(__MaxArcID, src, dst);
            src.Connections.Add(C);
            Connections.Add(__MaxArcID, C);
            ++(__MaxArcID);
            return C;
        }

        public CConnection AddConnection(CNode src, CNode dst, int srcArrivalTime, int dstArrivalTime, ref Carpools.Carpooler Pooler, Dictionary<string, string> tags)
        {

            __MaxArcID = Connections.Count + __NumCCDeleted + 1;

            CConnection C = new CConnection(__MaxArcID, src, dst, srcArrivalTime, dstArrivalTime, Pooler, tags);
            src.Connections.Add(C);
            Connections.Add(__MaxArcID, C);
            CConnections.Add(__MaxArcID, C);
            Pooler.networkIdCCList.Add(__MaxArcID);
            ++(__MaxArcID);
            return C;
        }

        public void RemoveConnection(Carpools.Carpooler Pooler)
        {
            /* Remove all CConnections and Connections related the deleted Pooler */
            foreach(long id in Pooler.networkIdCCList)
            {
                bool removedFromCConnection = false;
                bool removedFromConnection = false;

                if (CConnections.ContainsKey(id))
                {
                    CConnections.Remove(id);
                    //log.Info("Pooler=" + Pooler.Id + ": removed connection id=" + id + " from CConnections");
                    removedFromCConnection = true;
                }

                if (Connections.ContainsKey(id))
                {
                    Connections.Remove(id);
                    //log.Info("Pooler=" + Pooler.Id + ": removed connection id=" + id + " from Connections");
                    removedFromConnection = true;
                }

                if (!removedFromCConnection)
                    throw new Exception("Removing Carpooler ride from the CConnection network: ride not found (should be there!)");
                if (!removedFromConnection)
                    throw new Exception("Removing Carpooler ride from the Connection network: ride not found (should be there!)");

                /* Update the index */
                __NumCCDeleted++;


                /* Remove all Connections related the Pooler ride from all CNodes (and delete the CNode if no more connections are available) */
                List<long> CNodesKeyToRemove = new List<long> { };
                foreach (KeyValuePair<long, CNode> CN in CNodes)
                {
                    List<Connection> connToRemove = new List<Connection> { };

                    foreach (Connection C in CN.Value.Connections)
                    {
                        if (Pooler.networkIdCCList.Contains(C.Id))
                            connToRemove.Add(C);
                    }

                    if (connToRemove.Count > 0)
                    {
                        foreach (Connection C in connToRemove)
                        {
                            CN.Value.Connections.Remove(C);
                            //log.Info("Pooler=" + Pooler.Id + ": removed Connection id=" + C.Id + " from Cnode key=" + CN.Key);
                        }

                        if ( (CN.Value.Connections.Count == 0) || ((CN.Value.Connections.Count == 1) && (CN.Value.Connections[0] is LConnection)) )
                        {
                            /* Add the CNode to remove into a list (will be removed below) */
                            CNodesKeyToRemove.Add(CN.Key);

                            /* Remove the LConnection node from the Connections */
                            if (CN.Value.Connections[0] is LConnection)
                            {
                                if (Connections.ContainsKey(CN.Key))
                                {
                                    Connections.Remove(CN.Key);
                                    //log.Info("Pooler=" + Pooler.Id + ": removed CNode key=" + CN.Key + " from Connection");
                                    __NumCCDeleted++;
                                }
                            }

                        }

                    }
                }

                foreach (long cnodekey in CNodesKeyToRemove)
                {
                    if (CNodes.ContainsKey(cnodekey))
                    {
                        CNodes.Remove(cnodekey);
                        //log.Info("Removed CNode key=" + cnodekey);
                    }
                }
            }

            /* Remove the Pooler Connection */
            if (Carpoolers.Contains(Pooler))
            {
                Carpoolers.Remove(Pooler);
                log.Info("Carpooler id:" + Pooler.Id + " Name:" + Pooler.Name + " Provider:" + Pooler.Provider +
                    " Departure:" + Pooler.WayPointsOrig.First().Latitude + "," + Pooler.WayPointsOrig.First().Longitude +
                    " Destination:" + Pooler.WayPointsOrig.Last().Latitude + "," + Pooler.WayPointsOrig.Last().Longitude +
                    " TripDate:" + Pooler.TripDate + " TripStartTime:" + Pooler.TripStartTime + " removed from the network");
            }
            else
                throw new Exception("Removing Carpooler Carpoolers list: pooler not found (should be there!)");
        }


        /* 
         * Add the traffic report into the RoutingNetwork and for each Connection type (CC, RC, TC). This is obtained by adding them to the Connection
         * Just 1 traffic report per connection is handled; if there are more, the worst case is considered.
         */
        public void AddTrafficReport(Traffic.TrafficReport TrafficReport)
        {
            this.TrafficReport.Add(TrafficReport);
            long numConnectionUpdated = 0;
            long numCConnectionUpdated = 0;
            long numRConnectionUpdated = 0;
            long numTConnectionUpdated = 0;

            /* Add TrafficReport to the Connections */
            foreach (KeyValuePair<long, Connection> C in Connections)
            {
                /* Check if the "as the crow flies" distance between the traffic report and the connection source or destination is < deltaTrafficZone */
                Node Source = C.Value.GetSource();
                Node Destination = C.Value.GetDestination();

                double distanceFromSource = Source.Point.DistanceFrom(TrafficReport.Coordinates);
                double distanceFromDestination = Destination.Point.DistanceFrom(TrafficReport.Coordinates);

                if ( !(C.Value is LConnection) && ((distanceFromSource < TrafficReport.TrafficPropagationMaxDistance) || (distanceFromDestination < TrafficReport.TrafficPropagationMaxDistance)) )
                {
                    if ((C.Value.getTrafficReport() == null) || ((C.Value.getTrafficReport() != null) && (TrafficReport.Severity > C.Value.getTrafficReport().Severity)))
                    {
                        C.Value.setTrafficReport(TrafficReport);
                        C.Value.setTrafficDistanceFromSource(distanceFromSource);
                        C.Value.setTrafficDistanceFromDestination(distanceFromDestination);

                        numConnectionUpdated++;

                        if (C.Value is CConnection)
                        {
                            numCConnectionUpdated++;
                            //log.Info("CONNECTION: added Traffic report to the carpooling connection between " + ((CNode)C.Value.GetSource()).Id + " and " + ((CNode)C.Value.GetDestination()).Id);
                        }
                        else if (C.Value is TConnection)
                        {
                            numTConnectionUpdated++;
                            //log.Info("CONNECTION: Added Traffic report to the PT connection between " + ((TNode)C.Value.GetSource()).StopName + " and " + ((TNode)C.Value.GetDestination()).StopName);
                        }
                        else if (C.Value is RConnection)
                        {
                            numRConnectionUpdated++;
                            //log.Info("CONNECTION: Added Traffic report to the Rconnection between " + ((RNode)C.Value.GetSource()).Id + " and " + ((RNode)C.Value.GetDestination()).Id);
                        }
                        else
                        {
                            //log.Warn("CONNECTION: Added Traffic report to the connection");
                        }
                    }
                }
            }

            log.Info("TrafficReport Id:" + TrafficReport.Id + 
                     " category:" + TrafficReport.Category + 
                     " severity:" + TrafficReport.Severity + 
                     " address:" + TrafficReport.Address + 
                     " coordinates:" + TrafficReport.Coordinates.Latitude + "," + TrafficReport.Coordinates.Longitude + 
                     " - " + " ConnectionUpdated:" + numConnectionUpdated + " (CConnection:" + numCConnectionUpdated + 
                     " RConnection:" + numRConnectionUpdated + " TConnection:" + numTConnectionUpdated  + ")");
        }

        /* 
         * Remove the traffic report from the RoutingNetwork and from each Connection type (CC, RC, TC, LC)
         */
        public void RemoveTrafficReport(Traffic.TrafficReport TrafficReport)
        {
            this.TrafficReport.Remove(TrafficReport);
            long numConnectionRemoved = 0;
            long numCConnectionRemoved = 0;
            long numRConnectionRemoved = 0;
            long numTConnectionRemoved = 0;
            Traffic.TrafficReport TrafficReportNull = null;

            /* Remove TrafficReport from the Connections */
            foreach (KeyValuePair<long, Connection> C in Connections)
            {
                if (!(C.Value is LConnection) && (C.Value.getTrafficReport() != null) && (C.Value.getTrafficReport().Id == TrafficReport.Id) )
                {
                    C.Value.setTrafficDistanceFromDestination(0);
                    C.Value.setTrafficDistanceFromSource(0);
                    C.Value.setTrafficReport(TrafficReportNull);

                    numConnectionRemoved++;

                    if (C.Value is CConnection)
                    {
                        numCConnectionRemoved++;
                    }
                    else if (C.Value is TConnection)
                    {
                        numTConnectionRemoved++;
                    }
                    else if (C.Value is RConnection)
                    {
                        numRConnectionRemoved++;
                    }

                }
            }

            log.Info("TrafficReport Id:" + TrafficReport.Id + " category:" + TrafficReport.Category + " severity:" + TrafficReport.Severity + " - " + numConnectionRemoved +
                " Connection updated (" + numCConnectionRemoved + " CConnection, " + numRConnectionRemoved + " RConnection, " + numTConnectionRemoved + " TConnection)");
        }


        public void Serialize()
        {
            string Path = System.IO.Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory + ConfigurationManager.AppSettings["dataPath"] + "\\" + ConfigurationManager.AppSettings["NetworkFileName"]);
            IFormatter formatter = new BinaryFormatter();
            Stream stream = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.None);
            formatter.Serialize(stream, this);
            stream.Close();
        }

        public static RoutingNetwork DeSerialize()
        {
            string Path = System.IO.Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory + ConfigurationManager.AppSettings["dataPath"] + "\\" + ConfigurationManager.AppSettings["NetworkFileName"]);
            IFormatter formatter = new BinaryFormatter();
            Stream stream = new FileStream(Path,
                                      FileMode.Open,
                                      FileAccess.Read,
                                      FileShare.Read);
            RoutingNetwork Network = (RoutingNetwork)formatter.Deserialize(stream);
            stream.Close();

            return Network;
        }


        public void duplicateNetwork(ref RoutingNetwork rn2)
        {
            rn2 = new RoutingNetwork { };

            rn2.GNodes = new Dictionary<long, Node>();
            foreach (KeyValuePair<long, Node> GN in GNodes)
                rn2.GNodes.Add(GN.Key, GN.Value);

            rn2.Connections = new Dictionary<long, Connection>();
            foreach (KeyValuePair<long, Connection> C in Connections)
                rn2.Connections.Add(C.Key, C.Value);

            rn2.RNodes = new Dictionary<long, RNode>();
            foreach (KeyValuePair<long, RNode> RN in RNodes)
                rn2.RNodes.Add(RN.Key, RN.Value);

            rn2.RConnections = new Dictionary<long, RConnection>();
            foreach (KeyValuePair<long, RConnection> RC in RConnections)
                rn2.RConnections.Add(RC.Key, RC.Value);

            rn2.TNodes = new Dictionary<long, TNode>();
            foreach (KeyValuePair<long, TNode> TN in TNodes)
                rn2.TNodes.Add(TN.Key, TN.Value);

            rn2.TConnections = new Dictionary<long, TConnection>();
            foreach (KeyValuePair<long, TConnection> TC in TConnections)
                rn2.TConnections.Add(TC.Key, TC.Value);

            rn2.CNodes = new Dictionary<long, CNode>();
            foreach (KeyValuePair<long, CNode> CN in CNodes)
                rn2.CNodes.Add(CN.Key, CN.Value);

            rn2.CConnections = new Dictionary<long, CConnection>();
            foreach (KeyValuePair<long, CConnection> CC in CConnections)
                rn2.CConnections.Add(CC.Key, CC.Value);

            rn2.Carpoolers = new List<Carpools.Carpooler>();
            foreach (Carpools.Carpooler CP in Carpoolers)
                rn2.Carpoolers.Add(CP);

            rn2.SpatialQuadTree = new Containers.RTree.RTree<RNode>();
            rn2.SpatialQuadTree = SpatialQuadTree;


            rn2.MinPoint = new Point(MinPoint.Latitude, MinPoint.Longitude);
            rn2.MaxPoint = new Point(MaxPoint.Latitude, MaxPoint.Longitude); 
            
            rn2.__MaxArcID = __MaxArcID;
            rn2.__NumCCDeleted = __NumCCDeleted;
        }



        //DEPRECATED
        //public string GetConnectionAttributes(CNode node)
        //{
        //    string address = null;

        //    foreach (KeyValuePair<long, Connection> C in Connections)
        //    {
        //        if ( ((node.Point.Latitude == C.Value.GetSource().Point.Latitude) && (node.Point.Longitude == C.Value.GetSource().Point.Longitude)) ||
        //                ((node.Point.Latitude == C.Value.GetDestination().Point.Latitude) && (node.Point.Longitude == C.Value.GetDestination().Point.Longitude)) )
        //        {
        //            address = C.Value.GetTagValue("addr:housenumber") + "," + C.Value.GetTagValue("addr:street");
        //            if (C.Value.GetTagValue("addr:street") == null)
        //                address = C.Value.GetTagValue("name");

        //            break;
        //        }

        //    }

        //    return address;
        //}

        public Dictionary<string, string> GetConnectionTags(CNode src, CNode dst)
        {
            Dictionary<string, string> tags = null;

            foreach (KeyValuePair<long, Connection> C in Connections)
            {
                if (((src.Point.Latitude == C.Value.GetSource().Point.Latitude) && (src.Point.Longitude == C.Value.GetSource().Point.Longitude)) ||
                        ((dst.Point.Latitude == C.Value.GetDestination().Point.Latitude) && (dst.Point.Longitude == C.Value.GetDestination().Point.Longitude)))
                {
                    tags = new Dictionary<string, string>(C.Value.Tags);
                    //break;
                }

            }

            return tags;
        }

        public List<TNodeCarpooling> findClosestStop(Point startPoint)
        {
            List<TNodeCarpooling> PTnodes = new List<TNodeCarpooling>();

            foreach (KeyValuePair<long, TNodeCarpooling> node in TNodesCarpooling)
            {
                double distance = startPoint.DistanceFrom(node.Value.Point);
                TNodeCarpooling PTnode = new TNodeCarpooling(node.Value.Id, node.Value.Point, node.Value.StopId, node.Value.StopCode, node.Value.StopName, distance, node.Key);
                PTnodes.Add(PTnode);
            }

            PTnodes = PTnodes.OrderBy(x => x.distanceFromStartnode).ToList();

            return PTnodes;
        }

        /*
         * Returns the nearest TNode stop
         */
        public TNode findClosestPTStopNode(Point startPoint)
        {
            long TnodeId = 0;
            double mindistance = double.MaxValue;

            foreach (KeyValuePair<long, TNode> node in TNodes)
            {
                double distance = startPoint.DistanceFrom(node.Value.Point);
                if (distance < mindistance)
                {
                    TnodeId = node.Key;
                    mindistance = distance;
                }
            }


            TNode tnode = null;
            TNodes.TryGetValue(TnodeId, out tnode);

            return tnode;
        }

        /*
        public bool findConnection(Point startingPoint, Point destinationPoint)
        {
            bool found = false;

            foreach(KeyValuePair<long,TConnection> C in TConnections)
            {
                //if ( (C.Value.GetSource().Point.Latitude == startingPoint.Latitude) && (C.Value.GetSource().Point.Longitude == startingPoint.Longitude) &&
                //     (C.Value.GetDestination().Point.Latitude == startingPoint.Latitude) && (C.Value.GetDestination().Point.Longitude == startingPoint.Longitude) )
                TNode tnodeS = (TNode)C.Value.GetSource();
                TNode tnodeD = (TNode)C.Value.GetDestination();
                if (string.Compare(tnodeS.StopName, "Kirkcaldy, Stance 5 Bus Station") == 0)
                {
                    if (string.Compare(tnodeD.StopName, "Dalgety Bay, opp Railway Station on A921") == 0)
                    {
                        found = true;
                    }

                }
            }

            foreach (KeyValuePair<long, TNode> tn in TNodes)
            {
                if (string.Compare(tn.Value.StopName, "Dalgety Bay, opp Railway Station on A921") == 0)
                {
                    found = true;
                }
            }

            return found;
        }
        */


        /* 
         *  Create the network for the carpooling network based on PT stops
         */
        public void CreateNetworkForCarpoolingRides()
        {
            /* Duplicate all TNodes into the TNodesCarpooling list */
            foreach (KeyValuePair<long, TNode> TN in TNodes)
                AddNodeCarpooling(TN.Value.Id, TN.Value.Point, TN.Value.StopId, TN.Value.StopName);


            foreach (KeyValuePair<long,TNodeCarpooling> TNC in TNodesCarpooling)
            {
                /* Find the PT stops list nearby */
                List<TNodeCarpooling> PTstops = findClosestStop(TNC.Value.Point);
                //List<TNodeCarpooling> PTstops = findClosestStop(TNC.Value.Point);
                int numConnectionsAdded = 0;

                foreach (TNodeCarpooling PTs in PTstops)
                {
                    if (string.Compare(PTs.StopName.ToLower(), TNC.Value.StopName.ToLower()) != 0)
                    {
                        if ( (PTs.distanceFromStartnode < Carpools.CarpoolParser.CARPOOLING_MAX_DISTANCE_PTCONNECTIONS) || (numConnectionsAdded < CarpoolingMaxConnectionsPerTNode) )
                        {
                            this.AddConnectionForCarpoolingRides(TNC.Value, TNodesCarpooling[PTs.keyTmp]);
                            this.AddConnectionForCarpoolingRides(TNodesCarpooling[PTs.keyTmp], TNC.Value);
                            //log.Warn("Added connection from: \"" + TN.Value.StopName + "\" to \"" + PTs.StopName + "\" (" + PTs.distanceFromStartnode + "m)");
                            numConnectionsAdded++;
                        }
                        else
                            break;
                    }
                }

                ///* If any node distance is less then CARPOOLING_MAX_DISTANCE_PTCONNECTIONS, add at least the first one/two */
                //if (numConnectionsAdded == 0)
                //{
                //    int i = 0;
                //    int add = 0;
                //    while ( (add < 2) && (i < 100) )
                //    {
                //        if (string.Compare(TN.Value.StopName, PTstops[i].StopName) != 0)
                //        {
                //            this.AddConnectionForCarpoolingRides(TN.Value, PTstops[i]);
                //            log.Warn("Connection missing: added connections from \"" + TN.Value.StopName + "\" to \"" + PTstops[i].StopName + "\" (" + PTstops[i].distanceFromStartnode + "m)");
                //            add++;
                //        }

                //        i++;
                //    }
                //}

            }                
        }
    }
}
