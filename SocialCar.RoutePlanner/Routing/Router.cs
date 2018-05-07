using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using log4net;
using log4net.Config;

using SocialCar.RoutePlanner.Routing.Nodes;
using SocialCar.RoutePlanner.Routing.Connections;
using System.Text.RegularExpressions;

namespace SocialCar.RoutePlanner.Routing
{

    [Serializable]
    public enum TravelMode
    {
        None = 0,
        Car = 1,
        Walking = 2,
        Bus = 4,
        Carpool = 8
    };

    [Serializable]
    public enum ObjFunction
    {
        Distance = 0,
        Time = 1,
        Weighted = 2
    };

    public class WeightedObjFunctionParams
    {
        private float WalkingFactor;
        private float TransportationFactor;
        private float TransportationChangeFactor;
        private float CarpoolingFactor;

        public WeightedObjFunctionParams (float WalkingFactor, float TransportationFactor, float TransportationChangeFactor, float CarpoolingFactor)
        {
            this.WalkingFactor = WalkingFactor;
            this.TransportationFactor = TransportationFactor;
            this.TransportationChangeFactor = TransportationChangeFactor;
            this.CarpoolingFactor = CarpoolingFactor;
        }

        public float getWalkingFactor()
        {
            return this.WalkingFactor;
        }

        public float getTransportationFactor()
        {
            return this.TransportationFactor;
        }

        public float getTransportationChangeFactor()
        {
            return this.TransportationChangeFactor;
        }

        public float getCarpoolingFactor()
        {
            return this.CarpoolingFactor;
        }
    }
    
    public class Router
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        public static readonly string stringSeparator = "###";

        private Node Source;
        private Node Target;
        private string Date;
        private double StartTime;
        private TravelMode Modes;
        private ObjFunction ObjFunc;

        public List<Connection> Solution;
        public List<RoutingPath> RoutingPaths;
        
        public static Message RPMessage;

        public Message getRPMessage()
        {
            return RPMessage;
        }

        public RouterInfo RI;

        public Router(Node Source, Node Target, string Date, double StartTime, TravelMode Modes, ObjFunction Func, int index = 0, RouterFactors Param = null)
        {
            this.Source = Source;
            this.Target = Target;
            this.Date = Date;
            this.StartTime = StartTime;
            this.ObjFunc = Func;
            this.Modes = Modes;
            RPMessage = new Message { };
            RI = new RouterInfo((int)StartTime, Date, ObjFunc, Param);
            if (RI.routerFactors != null)
                this.RI.routerFactors.Index = index;
        }

        public double SolutionCost { get; private set; }

        public void Start()
        {
            Solution = GetPath(Modes, Date, StartTime);

            RoutingPaths = new List<RoutingPath>();
            RoutingPath RP = new RoutingPath(Solution, Date, StartTime, Modes);
            RoutingPaths.Add(RP);
        }

        private List<Connection> GetPath(TravelMode Modes, string date, double startTime)
        {
            try
            {
                RI.Parent.Add(Source, null);
                RI.ArrivalTime.Add(Source, startTime);
                RI.Q[Source] = 0.0;

                while (RI.Q.Count != 0)
                {
                    // find the closest reached unfixed node and fix it.
                    // Stop if target node found and fixed.
                    if (RI.FixNode(Target))
                        break;

                    foreach (Connection C in RI.u.Connections)
                        RI.Update(C, Modes, Target);
                }

                if (RI.ObjValues.ContainsKey(Target))
                    SolutionCost = RI.ObjValues[Target];
            }
            catch (Exception ex)
            {
                //TODO: add the ex into the message
                //throw ex;
            }

            List<Connection> RIList = RI.ConstructPath(Target);

            return RIList;
        }

        public class RouterInfo
        {
            public Satsuma.PriorityQueue<Node, double> Q = new Satsuma.PriorityQueue<Node, double>();
            public Dictionary<Node, double> ObjValues = new Dictionary<Node, double>();
            public Dictionary<Node, double> ArrivalTime = new Dictionary<Node, double>();
            public Dictionary<Node, Connection> Parent = new Dictionary<Node, Connection>();

            public Node u = null;
            public Node v = null;

            private double minObjValue, crntObjValue;

            private ObjFunction Func;

            public int StartTime;
            public string Date;

            public RouterFactors routerFactors;

            List<string> ListPTused;

            public RouterInfo(int StartTime, string Date, ObjFunction Func, RouterFactors routerFactor)
            {
                this.StartTime = StartTime;
                this.Date = Date;
                this.Func = Func;

                if (Func == ObjFunction.Weighted)
                    this.routerFactors = new RouterFactors(routerFactor.WalkingFactor, routerFactor.TransportationFactor,
                                                            routerFactor.TransportationChangeFactor, routerFactor.CarpoolingFactor,
                                                            routerFactor.TrafficPropagationMaxDistance, routerFactor.Index,
                                                            routerFactor.SecondsForward);
                //RouterFactors.ReadFactorsFromConfig();

                ListPTused = new List<string>();
            }

            public bool FixNode(Node Target)
            {
                u = Q.Peek(out minObjValue);
                Q.Pop();

                if (!ObjValues.ContainsKey(u))
                    ObjValues.Add(u, minObjValue);
                else
                    ObjValues[u] = minObjValue;

                if (u == Target)
                    return true;

                return false;
            }

            private double GetObjValueBasedOnDistance(Connection C, TravelMode Modes)
            {
                //if (!(C is RConnection))
                /* Added TConnection in order to also consider carpooling Tconnection network */
                if (!(C is RConnection) && !(C is TConnectionForCarpooling))
                    return double.PositiveInfinity;

                return minObjValue + C.GetDistance(Modes);
            }

            private double GetObjValueBasedOnTime(Connection C, TravelMode Modes)
            {
                return minObjValue + C.GetTravelTime(Date, ArrivalTime[C.GetSource()], Modes);
            }

            private double GetWeightedObjValue(Connection C, TravelMode Modes)
            {
                double Time = C.GetTravelTime(Date, ArrivalTime[C.GetSource()], Modes);

                /* GetTravelTime returns an infinity time if the route should not be considered */
                if (Time != double.PositiveInfinity)
                {
                    if (C is RConnection)
                    {
                        //Time *= routerFactors.WalkingFactor;
                        Time += 30; //TEST
                    }
                    else if (C is TConnection)
                    {
                        /* This is a fix introduced in order to skip the Mobalt buses (which are considered carpooling means) if the weight is >=50 */
                        if (string.Compare(C.GetTimeTable().Entries.First().AgencyId, "Mobalt###Mobalt") == 0)
                        {
                            if (routerFactors.CarpoolingFactor >= 50)
                                return double.PositiveInfinity;
                        }

                        if (Parent[C.GetSource()] != null)
                        {
                            if (Parent[C.GetSource()] is TConnection)
                            {
                                TConnection PC = Parent[C.GetSource()] as TConnection;
                                if ((C as TConnection).TimeTable.RouteId != PC.TimeTable.RouteId)
                                {
                                    //Time *= routerFactors.TransportationChangeFactor; //BUG!!! If Time==0 => no delay added for transportation changes
                                    Time = (Time > 60) ? Time : 60; // since we have 1 minute step, add 1 minute delay if the original Time is 0 and we have a transportation change
                                    Time *= routerFactors.TransportationChangeFactor;
                                    //Time += routerFactors.TransportationChangeFactor; //TEST add X seconds for each PT change
                                }
                            }

                            Time *= routerFactors.TransportationFactor;
                            //Time += routerFactors.TransportationFactor*3; //TEST add X seconds for each PT change
                        }
                    }
                    else if (C is CConnection)
                    {
                        if (Parent[C.GetSource()] != null)
                        {
                            if (Parent[C.GetSource()] is CConnection)
                            {
                                CConnection PC = Parent[C.GetSource()] as CConnection;

                                Time *= routerFactors.CarpoolingFactor;

                                if ((string.Compare(C.GetCarpoolerId(), PC.GetCarpoolerId()) != 0))
                                {
                                    return double.PositiveInfinity;
                                }
                            }
                        }
                    }

                }

                double ObjValue = minObjValue + Time;

                return ObjValue;
            }

            /* Starting from the connection C, check if there is the same PT twice in the parent TimeTable nodes (considering a PT change in the middle)
             * the trip should not allow get off and get on again on the same bus
             */
            public bool checkConstraints(Connection C)
            {
                bool cutConnection = false;
                bool PTalreadyTaken = false;
                bool CCalreadyTaken = false;
                bool TooMuchPT = false;
                Node node = v;
                Connection ParentC = null;
                string RouteId = null;
                bool changeCC = false;
                int countParentNodes = 0;
                List<Connection> Clist = new List<Connection>(); //used to temporarily save the parent connections
                List<string> PTusedList = new List<string>();
                List<string> PTusedDistinctList = new List<string>();
                List<string> CCusedList = new List<string>();
                //string PrevParentCRouteShortName = null;
                string PrevParentCRouteId = null;
                bool oneCCtaken = false;

                /* Save the current connection details */
                if (Parent.ContainsKey(node))
                {
                    ParentC = Parent[node];

                    /* Get specific details for the PT connections */
                    if (C is TConnection)
                    {
                        //RouteShortName = C.GetTimeTableEntry().RouteShortName;
                        RouteId = Regex.Split(C.GetRouteId(), stringSeparator).Last();
                    }
                }

                /* Check if the same PT was already taken (PTalreadyTaken) and/or there is another carpooling ride into the trip (CCalreadyTaken) */
                if (ParentC != null)
                {
                    do
                    {
                        ParentC = Parent[node];

                        if (ParentC != null)
                        {
                            countParentNodes++;
                            Clist.Add(ParentC);

                            if (ParentC is TConnection)
                            {
                                //string ParentCRouteShortName = ParentC.GetTimeTableEntry().RouteShortName;
                                //string ParentCRouteShortName = Regex.Split(ParentC.GetRouteId(), stringSeparator).Last(); 
                                string ParentCRouteId = Regex.Split(ParentC.GetRouteId(), stringSeparator).Last();

                                /* Add into the PTusedList all elements */
                                //PTusedList.Add(ParentCRouteShortName);
                                PTusedList.Add(ParentCRouteId);

                                //log.Info("ParentCRouteShortName:" + ParentCRouteShortName);

                                //if ((PrevParentCRouteShortName != null) && (ParentCRouteShortName != PrevParentCRouteShortName))
                                if ((PrevParentCRouteId != null) && (ParentCRouteId != PrevParentCRouteId))
                                {
                                    /* Add into the PTusedDistinctList unique elements */
                                    //if (!PTusedDistinctList.Contains(PrevParentCRouteShortName))
                                    if (!PTusedDistinctList.Contains(PrevParentCRouteId))
                                    {
                                        //PTusedDistinctList.Add(PrevParentCRouteShortName);
                                        PTusedDistinctList.Add(PrevParentCRouteId);
                                        //log.Info("(Tcon) Added distinct list PrevParentCRouteShortName:" + PrevParentCRouteShortName);
                                    }
                                    else
                                    {
                                        PTalreadyTaken = true;
                                        //log.Info("(Tcon) PT already taken: PrevParentCRouteShortName:" + PrevParentCRouteShortName + "   PTusedDistinctList: (" + string.Join(", ", PTusedDistinctList.ToArray()) + ")");
                                    }
                                }

                                //PrevParentCRouteShortName = ParentCRouteShortName;
                                PrevParentCRouteId = ParentCRouteId;
                            }
                            else if (ParentC is CConnection)
                            {
                                /* Add into the PTusedList all elements */
                                PTusedList.Add(ParentC.GetCarpoolerId());

                                if (!CCusedList.Contains(ParentC.GetCarpoolerId()))
                                    CCusedList.Add(ParentC.GetCarpoolerId());

                                /* If after a PT change there is another carpooling connection, cut the connection */
                                if ((changeCC) && (ParentC is CConnection))
                                    CCalreadyTaken = true;

                                oneCCtaken = true;
                            }
                            else if (ParentC is LConnection)
                            {
                                /* Add into the PTusedList all elements */
                                PTusedList.Add("LConn");

                                //if (PrevParentCRouteShortName != null)
                                if (PrevParentCRouteId != null)
                                {
                                    /* Add the PT to the unique list if there is a PT change from PT to !PT mean */
                                    //if (!PTusedDistinctList.Contains(PrevParentCRouteShortName))
                                    if (!PTusedDistinctList.Contains(PrevParentCRouteId))
                                    {
                                        //PTusedDistinctList.Add(PrevParentCRouteShortName);
                                        PTusedDistinctList.Add(PrevParentCRouteId);
                                        //log.Info("(!TCon) Added distinct list PrevParentCRouteShortName:" + PrevParentCRouteShortName);
                                    }
                                    else
                                    {
                                        PTalreadyTaken = true;
                                        //PTusedDistinctList.Add(PrevParentCRouteShortName);
                                        PTusedDistinctList.Add(PrevParentCRouteId);
                                        //log.Info("(!Tcon) PT already taken: PrevParentCRouteShortName:" + PrevParentCRouteShortName + "   PTusedDistinctList: (" + string.Join(", ", PTusedDistinctList.ToArray()) + ")");
                                    }
                                }
                            }
                            else if (ParentC is RConnection)
                            {
                                /* Add into the PTusedList all elements */
                                PTusedList.Add("RConn");

                                //if (PrevParentCRouteShortName != null)
                                if (PrevParentCRouteId != null)
                                {
                                    /* Add the PT to the unique list if there is a PT change from PT to !PT mean */
                                    //if (!PTusedDistinctList.Contains(PrevParentCRouteShortName))
                                    if (!PTusedDistinctList.Contains(PrevParentCRouteId))
                                    {
                                        //PTusedDistinctList.Add(PrevParentCRouteShortName);
                                        PTusedDistinctList.Add(PrevParentCRouteId);
                                        //log.Info("(!TCon) Added distinct list PrevParentCRouteShortName:" + PrevParentCRouteShortName);
                                    }
                                    else
                                    {
                                        PTalreadyTaken = true;
                                        //PTusedDistinctList.Add(PrevParentCRouteShortName);
                                        PTusedDistinctList.Add(PrevParentCRouteId);
                                        //log.Info("(!Tcon) PT already taken: PrevParentCRouteShortName:" + PrevParentCRouteShortName + "   PTusedDistinctList: (" + string.Join(", ", PTusedDistinctList.ToArray()) + ")");
                                    }
                                }
                            }


                            /* If PT are > N cut */
                            //if (PTusedDistinctList.Count > 2)
                                //TooMuchPT = true;

                            if (!(ParentC is TConnection))
                                PrevParentCRouteId = null; //PrevParentCRouteShortName = null;

                            if ( ((oneCCtaken) && !(ParentC is CConnection)) || ((ParentC is CConnection) && (C is CConnection) && (ParentC.GetCarpoolerId() != C.GetCarpoolerId()) ) )
                                changeCC = true; //PrevParentCRouteShortName = null;

                            /* Get the prev node (ParentC is never null at this point) */
                            node = Parent[node].GetSource();

                        } else
                            node = null;

                    }
                    while ((node != null) && (Parent.ContainsKey(node)) && (!PTalreadyTaken) && (!CCalreadyTaken) && (!TooMuchPT) && (countParentNodes < 1000)); // countParentNodes<1000 => added as a precaution to avoid loops
                }


                /* If the above constraints are satisfied (so there is not the same PT twice and there is not more than 1 carpooling ride)
                 * then go on, otherwise cut this connection (this results on an infinite ObjValue downstream)
                 */
                if (PTalreadyTaken || CCalreadyTaken || TooMuchPT)
                {
                    cutConnection = true;

                    if (CCalreadyTaken)
                    {
                        log.Info("----------------------------------------------------------------------------------------");
                        log.Info("PTusedDistinctList: (" + string.Join(", ", PTusedDistinctList.ToArray()) + ")");
                        log.Info("PTusedList: (" + string.Join(", ", PTusedList.ToArray()) + ")");
                        //for (int i = 0; i < (PTusedList.Count - 2); i++)
                        //{
                        //    log.Info("PTusedDistinctList: (" + string.Join(", ", PTusedDistinctList.ToArray()) + ")");
                        //    log.Info("PTusedList: (" + string.Join(", ", PTusedList.ToArray()) + ")");
                        //    log.Info("----------------------------------------------------------------------------------------");
                        //    log.Info("ci siamo");
                        //    break;
                        //}
                    }
                } else
                {
                    //if (CCusedList.Count > 1)
                    //{
                    //    log.Info("----------------------------------------------------------------------------------------");
                    //    log.Info("CCusedList: (" + string.Join(", ", CCusedList.ToArray()) + ")");
                    //    log.Info("PTusedList: (" + string.Join(", ", PTusedList.ToArray()) + ")");
                    //}

                }
                
                return cutConnection;
            }


            public void Update(Connection C, TravelMode Modes, Node Target = null)
            {
                v = C.GetDestination();

                if (ObjValues.ContainsKey(v))
                    return; // already processed

                if (!Q.TryGetPriority(v, out crntObjValue))
                    crntObjValue = double.PositiveInfinity;

                double ObjValue = 0.0;

                if (Func == ObjFunction.Distance)
                    ObjValue = GetObjValueBasedOnDistance(C, Modes);
                else if (Func == ObjFunction.Time)
                    ObjValue = GetObjValueBasedOnTime(C, Modes);
                else if (Func == ObjFunction.Weighted)
                    ObjValue = GetWeightedObjValue(C, Modes);


                /* Check if the PT of this connection was already taken or there is at least one carpooling leg on the previous connections */
                /* WARNING: this function would take too much time to run */
                //bool cutConnection = false;
                //cutConnection = checkConstraints(C);
                //if (cutConnection)
                //    ObjValue = double.PositiveInfinity;


                if (ObjValue < crntObjValue)
                {
                    Q[v] = ObjValue;
                    if (Parent.ContainsKey(v))
                    {
                        Parent[v] = C;
                        ArrivalTime[v] = ArrivalTime[C.GetSource()] + C.GetTravelTime(Date, ArrivalTime[C.GetSource()], Modes);
                    }
                    else
                    {
                        Parent.Add(v, C);
                        ArrivalTime.Add(v, ArrivalTime[C.GetSource()] + C.GetTravelTime(Date, ArrivalTime[C.GetSource()], Modes));
                    }
                }
            }

            public List<Connection> ConstructPath(Node v, bool fixPTalreadyTaken=false)
            {
                LinkedList<Connection> Connections = new LinkedList<Connection>();
                Connection Connection = null;

                /* FIX: if the destination node is not included into the RP node list, get the closest point from 
                 *      the RP node listwhich should be the closest point to the destination node */
                bool found = false;
                List<RNode> ListNode = new List<RNode> { };

                if (Parent.ContainsKey(v))
                    found = true;
                else
                    found = false;

                if (!found)
                {
                    /* Set an info message into the router (only if this is not due to the PT already taken fix) */
                    if (!fixPTalreadyTaken)
                    { 
                        RPMessage.RoutePlannerCodes.Add(RPCodes.INFO_CONSTRUCT_PATH_FIX_TRIGGERED);
                        log.Info("The construct path fix has been triggered");
                    }

                    double distance;
                    double minDistance = Double.MaxValue;
                    Node newNode = null;
                    
                    /* Find the closest point into Parent */
                    foreach (var p in Parent)
                    {
                        distance = v.Point.DistanceFrom(p.Key.Point);
                        if (distance < minDistance)
                        {
                            newNode = p.Key;
                            minDistance = distance;
                        }
                    }

                    /* Replace the old point with the new one */
                    v = newNode;
                }
                /* End FIX */

                while (Parent[v] != null)
                {
                    Connection = Parent[v];
                    Connections.AddFirst(Connection);
                    v = Connection.GetSource();
                }
                
                return Connections.ToList();
            }
        }

        public class RouterFactors
        {
            public float WalkingFactor = 20;
            public float TransportationFactor = 10;
            public float TransportationChangeFactor = 10;
            public float CarpoolingFactor = 1;
            public double TrafficPropagationMaxDistance = 0;
            public int Index = 0;
            public int SecondsForward = 0;

            public RouterFactors(float WalkingFactor, float TransportationFactor, float TransportationChangeFactor, float CarpoolingFactor, double TrafficPropagationMaxDistance, int index, int secondsForward)
            {
                this.WalkingFactor = WalkingFactor;
                this.TransportationFactor = TransportationFactor;
                this.TransportationChangeFactor = TransportationChangeFactor;
                this.CarpoolingFactor = CarpoolingFactor;
                this.TrafficPropagationMaxDistance = TrafficPropagationMaxDistance;
                this.Index = index;
                this.SecondsForward = secondsForward;
            }

            //OBSOLETE
            //public static void ReadFactorsFromConfig()
            //{
            //    WalkingFactor = float.Parse(ConfigurationManager.AppSettings["WalkingFactor"]);
            //    TransportationFactor = float.Parse(ConfigurationManager.AppSettings["TransportationFactor"]);
            //    TransportationChangeFactor = float.Parse(ConfigurationManager.AppSettings["TransportationChangeFactor"]);
            //    CarpoolingFactor = float.Parse(ConfigurationManager.AppSettings["CarpoolingFactor"]);
            //}
        }

        //private List<List<SolutionLeg>> GetKPath(TravelMode Modes, string date, double startTime, int K)
        //{
        //    /// s: the source node.
        //    /// t: the destination node.
        //    /// K: the number of shortest paths to find.
        //    /// P_u: a path from s to u.
        //    /// B: is a heap data structure containing paths.
        //    /// P: set of shortest paths from s to t.
        //    /// count_u: number of shortest paths found to node u.

        //    Node s = Source;
        //    Node t = Target;

        //    List<RoutingPath> P = new List<RoutingPath>();
        //    ///  The number of shortest paths found to this node.
        //    Dictionary<Node, int> SCount = new Dictionary<Node, int>();
        //    Satsuma.PriorityQueue<RoutingPath, double> Q = new Satsuma.PriorityQueue<RoutingPath, double>();

        //    /// Insert path P_s = {s} into B with cost 0
        //    RoutingPath P_s = new RoutingPath(startTime);
        //    RConnection r = new RConnection(-1, new RNode(-1, s.Point), (RNode)s, true, true);

        //    P_s.AddConnectionAndIncreaseCost(r, 0);
        //    Q[P_s] = P_s.ObjectiveCost();

        //    /// Let P_u be the shortest cost path in B with cost C
        //    RoutingPath P_u = null;
        //    double weight = 0;
        //    /// while B is not empty and count_t < K:
        //    while (Q.Count != 0 && (SCount.ContainsKey(t) ? SCount[t] : 0) < K)
        //    {
        //        /// Let P_u be the shortest cost path in B with cost C
        //        /// B = B − {P_u}, count_u = count_u + 1

        //        // find the closest reached but unfixed node
        //        double minDist;
        //        P_u = Q.Peek(out minDist);
        //        Q.Pop();

        //        if (SCount.ContainsKey(P_u.GetPathTail())) SCount[P_u.GetPathTail()] += 1;
        //        else SCount.Add(P_u.GetPathTail(), 1);

        //        /// If u = t then P = P U Pu
        //        if (P_u.GetPathTail().Id == t.Id) P.Add(P_u);
        //        /// if count_u ≤ K then
        //        if (SCount[P_u.GetPathTail()] <= K)
        //        {
        //            /// for each vertex v adjacent to u:
        //            Node u = P_u.GetPathTail(); Node v = null;
        //            foreach (Connection C in u.Connections)
        //            {
        //                v = C.GetDestination();
        //                /// if v is not in P_u then
        //                if (!P_u.IsIn_P(v))
        //                {
        //                    /// Let P_v be a new path with cost C + w(u, v) formed by concatenating edge (u, v) to path P_u
        //                    weight = C.GetTravelTime(date, P_u.TotalCost, Modes);
        //                    // If invalid edge.
        //                    if (double.IsPositiveInfinity(weight)) continue;

        //                    RoutingPath P_v = P_u.GetCopy();

        //                    P_v.AddConnectionAndIncreaseCost(C, weight);
        //                    /// Insert P_v into B
        //                    Q[P_v] = P_v.ObjectiveCost();
        //                }
        //            }
        //        }
        //    }

        //    List<List<SolutionLeg>> Sols = new List<List<SolutionLeg>>();
        //    List<SolutionLeg> Sol;
        //    foreach (RoutingPath p in P)
        //    {
        //        Sol = new List<SolutionLeg>();
        //        foreach (SolutionLeg L in p.Legs)
        //            Sol.Add(L);
        //        Sols.Add(Sol);
        //    }

        //    this.RoutingPaths = P;
        //    return Sols;
        //}
    }

}
