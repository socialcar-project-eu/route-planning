using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;
using log4net.Config;

using SocialCar.RoutePlanner.Routing;
using SocialCar.RoutePlanner.Routing.Nodes;
using SocialCar.RoutePlanner.Routing.Connections;

namespace SocialCar.RoutePlanner.MapMatching
{
    public class HiddenMarkovState
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private Tuple<RNode, double> Candidate;
        public List<HiddenMarkovState> AdjacencyList {get; private set;}
        public Point P {get; private set;}
        //
        private bool IsVirtualState;

        public HiddenMarkovState(Point P, RNode roadSegment, double offset, bool isVirtual = false)
        {
            Candidate = new Tuple<RNode, double>(roadSegment, offset);
            AdjacencyList = new List<HiddenMarkovState>();
            IsVirtualState = isVirtual;
            this.P = P;
        }

        public void AddToAdjList(HiddenMarkovState State)
        {
            AdjacencyList.Add(State);
        }

        public bool IsVirtual()
        {
            return IsVirtualState;
        }

        public RNode GetUnderlyingRNode()
        {
            return Candidate.Item1;
        }

        /*****
        # A gaussian distribution
        def emission_prob(u):
            c = 1 / (SIGMA_Z * math.sqrt(2 * math.pi))
            return c * math.exp(-great_circle_distance(u.measurement, u)**2)
         *****/
        public double EmissionProbability()
        {
            if (IsVirtualState) return 1.0f;
            float sigma_z = 4.07f;
            double c = 1 / sigma_z * Math.Sqrt(2 * Math.PI);
            return c * Math.Exp(-Math.Pow(Candidate.Item1.Point.DistanceFrom(P), 2));
        }

        public double StateCost()
        {
            return -1 * Math.Log10(EmissionProbability());
        }
    }

    public class HiddenMarkovModelMapMatcher
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// Virtual start state.
        private HiddenMarkovState s;
        /// Virtual target state.
        private HiddenMarkovState t;
        private List<HiddenMarkovState>[] States;
        ///
        private RoutingNetwork RoadNetwork;
        private List<Point> Points;
        //
        private LinkedList<RNode> MatchingSolution;
        private LinkedList<TNodeCarpooling> MatchingSolutionTNodeCarpooling;

        public HiddenMarkovModelMapMatcher(ref RoutingNetwork roadNetwork, List<Point> points)
        {
            RoadNetwork = roadNetwork;
            States = new List<HiddenMarkovState>[points.Count];
            for (int i = 0; i < points.Count; ++i)
                States[i] = new List<HiddenMarkovState>();
            Points = points;
            MatchingSolution = new LinkedList<RNode>();
            MatchingSolutionTNodeCarpooling = new LinkedList<TNodeCarpooling>();
        }

        public List<RNode> Match()
        {
            int i = 0;
            try
            {
                for (i = 0; i < Points.Count - 1; ++i)
                {
                    SocialCar.RoutePlanner.Routing.Nodes.Point StartPoint =
                    new SocialCar.RoutePlanner.Routing.Nodes.Point(Points[i].Latitude, Points[i].Longitude);
                    SocialCar.RoutePlanner.Routing.Nodes.Point FinishPoint =
                        new SocialCar.RoutePlanner.Routing.Nodes.Point(Points[i + 1].Latitude, Points[i + 1].Longitude);

                    DateTime StartingTime = DateTime.Now;
                    int StartingTimeSinceMidnight = Globals.ConvertTimeToSeconds(StartingTime.ToString("HH:mm:ss"));

                    float delta = 0.1f;

                    RNode StartNode = null;
                    RNode TargetNode = null;
                    List<RNode> StartNodeList = new List<RNode> { };
                    List<RNode> TargetNodeList = new List<RNode> { };
                    StartNodeList = RoadNetwork.ResolvePoint(StartPoint, delta);
                    TargetNodeList = RoadNetwork.ResolvePoint(FinishPoint, delta);

                    if (StartNodeList.Count() > 0)
                        StartNode = StartNodeList.First();
                    if (TargetNodeList.Count() > 0)
                        TargetNode = TargetNodeList.First();

                    if ((StartNode != null) && (TargetNode != null))
                    {
                        Router X = new Router(StartNode, TargetNode, string.Empty, StartingTimeSinceMidnight, TravelMode.Car, ObjFunction.Distance);
                        X.Start();

                        if (X.Solution.Count() > 0)
                        {
                            foreach (Connection C in X.Solution)
                                MatchingSolution.AddLast((RNode)C.GetSource());
                            MatchingSolution.AddLast((RNode)X.Solution.Last().GetDestination());
                        }
                    }
                    else
                    {
                        throw new Exception("Carpooling StartNode or EndNode are null");
                    }
                }

                //Init();
                //RunDijkstra();
                //CheckConnectivity();
            }
            catch (Exception ex)
            {
                log.Error("ERROR: " + ex.Message);
                if (ex.InnerException != null)
                    log.Error("ERROR: " + ex.InnerException.Message);
                log.Error("ERROR: " + ex.StackTrace);
            }

            return MatchingSolution.ToList().Where(x => x != null).Distinct().ToList();
        }


        /*
         * Matching function for the carpooling rides
         */
        public List<TNodeCarpooling> MatchTNodeCarpooling()
        {
            int i = 0;
            try
            {
                for (i = 0; i < Points.Count - 1; ++i)
                {
                    SocialCar.RoutePlanner.Routing.Nodes.Point StartPoint =
                    new SocialCar.RoutePlanner.Routing.Nodes.Point(Points[i].Latitude, Points[i].Longitude);
                    SocialCar.RoutePlanner.Routing.Nodes.Point FinishPoint =
                        new SocialCar.RoutePlanner.Routing.Nodes.Point(Points[i + 1].Latitude, Points[i + 1].Longitude);

                    DateTime StartingTime = DateTime.Now;
                    int StartingTimeSinceMidnight = Globals.ConvertTimeToSeconds(StartingTime.ToString("HH:mm:ss"));

                    float delta = 0.1f;

                    TNodeCarpooling StartNode = null;
                    TNodeCarpooling TargetNode = null;
                    List<TNodeCarpooling> StartNodeList = new List<TNodeCarpooling> { };
                    List<TNodeCarpooling> TargetNodeList = new List<TNodeCarpooling> { };
                    StartNodeList = RoadNetwork.ResolvePointTNode(StartPoint, delta);
                    TargetNodeList = RoadNetwork.ResolvePointTNode(FinishPoint, delta);

                    if (StartNodeList.Count() > 0)
                        StartNode = StartNodeList.First();
                    if (TargetNodeList.Count() > 0)
                        TargetNode = TargetNodeList.First();

                    if ((StartNode != null) && (TargetNode != null))
                    {
                        Router X = new Router(StartNode, TargetNode, string.Empty, StartingTimeSinceMidnight, TravelMode.Carpool, ObjFunction.Distance);
                        X.Start();

                        if (X.Solution.Count() > 0)
                        {
                            foreach (Connection C in X.Solution)
                                MatchingSolutionTNodeCarpooling.AddLast((TNodeCarpooling)C.GetSource());
                            MatchingSolutionTNodeCarpooling.AddLast((TNodeCarpooling)X.Solution.Last().GetDestination());
                        }
                    }
                    else
                    {
                        throw new Exception("Carpooling StartNode or EndNode are null");
                    }
                }

                //Init();
                //RunDijkstra();
                //CheckConnectivity();
            }
            catch (Exception ex)
            {
                log.Error("ERROR: " + ex.Message);
                if (ex.InnerException != null)
                    log.Error("ERROR: " + ex.InnerException.Message);
                log.Error("ERROR: " + ex.StackTrace);
            }

            return MatchingSolutionTNodeCarpooling.ToList().Where(x => x != null).Distinct().ToList();
        }

        private void Init()
        {
            s = new HiddenMarkovState(null, null, 0.0f, true);
            t = new HiddenMarkovState(null, null, 0.0f, true);
            foreach (Point P in Points)
            {
                CreateState(P, Points.IndexOf(P));
            }
            LinkStates();
        }

        private void CreateState(Point P, int i)
        {
            List<RNode> Candidates = RoadNetwork.ResolvePoint(P, 0.1f, 3);
            foreach (RNode n in Candidates)
                States[i].Add(new HiddenMarkovState(P, n, P.DistanceFrom(n.Point)));
        }

        private void LinkStates()
        {
            // Link virtual start state.
            foreach (HiddenMarkovState S in States[0])
                s.AddToAdjList(S);
            // Link internal states.
            List<HiddenMarkovState> uList = null;
            for (int i = 0; i < Points.Count - 1; ++i)
            {
                uList = States[i];
                foreach (HiddenMarkovState S in uList)
                {
                    foreach (HiddenMarkovState S1 in States[i + 1])
                    {
                        S.AddToAdjList(S1);
                    }
                }
            }
            // Link target state.
            foreach (HiddenMarkovState S in States[Points.Count - 1])
                S.AddToAdjList(t);
        }

        private void RunDijkstra()
        {
            Satsuma.PriorityQueue<HiddenMarkovState, double> Q = new Satsuma.PriorityQueue<HiddenMarkovState, double>();
            Dictionary<HiddenMarkovState, double> distance = new Dictionary<HiddenMarkovState, double>();
            Dictionary<HiddenMarkovState, HiddenMarkovState> parent = new Dictionary<HiddenMarkovState, HiddenMarkovState>();

            if (!parent.ContainsKey(s))
                parent.Add(s, null);
            else
                parent[s] = null;

            Q[s] = 0.0;

            while (Q.Count != 0)
            {
                // find the closest reached but unfixed node
                double minDist;
                HiddenMarkovState min = Q.Peek(out minDist);
                Q.Pop();

                /// fix the node.
                if (!distance.ContainsKey(min))
                    distance.Add(min, minDist);
                else
                    distance[min] = minDist; // fix the node

                if (min == t) break; // target node found and fixed.

                foreach (HiddenMarkovState v in min.AdjacencyList)
                {
                   if (distance.ContainsKey(v)) continue; // already processed

                    double newDist = minDist + min.StateCost() + EdgeCost(min, v);

                    double oldDist;
                    if (!Q.TryGetPriority(v, out oldDist)) oldDist = double.PositiveInfinity;

                    if (newDist < oldDist)
                    {
                        Q[v] = newDist;
                        if (parent.ContainsKey(v))
                            parent[v] = min;
                        else
                            parent.Add(v, min);
                    }
                }
            }

            // Construct Solution.
            HiddenMarkovState S = parent[t];
            while (S != null)
            {
                //if (S.GetUnderlyingRNode() != null);
                MatchingSolution.AddFirst(S.GetUnderlyingRNode());
                S = parent[S];
            }
        }

        private void CheckConnectivity()
        {
            List<RNode> Nodes = MatchingSolution.Where(x => x != null).ToList();
            List<Tuple<RNode, RNode>> Cuts = new List<Tuple<RNode, RNode>>();

            RNode u = null, v = null; RConnection C = null;
            for (int i = 0; i < Nodes.Count - 1; ++i)
            {
                u = Nodes[i]; v = Nodes[i + 1];
                C = RoadNetwork.AreConnected(u, v);
                if (C == null)
                    Cuts.Add(new Tuple<RNode, RNode>(u, v));
            }
            //
            foreach (Tuple<RNode, RNode> T in Cuts)
            {
                u = T.Item1; v = T.Item2;

                Node Source = RoadNetwork.ResolvePoint(u.Point, 0.1f).First();
                Node Target = RoadNetwork.ResolvePoint(v.Point, 0.1f).First();

                Router router = new Router(Source, Target, string.Empty, 0, TravelMode.Car, ObjFunction.Distance);
                router.Start();

                List<RNode> route = new List<RNode>();
                foreach (Connection conn in router.Solution)
                {
                    route.Add((RNode)conn.GetDestination());
                }
                route.RemoveAll(x => x == u || x == v);
                RNode prev = u, next = null;
                for (int i = 0; i < route.Count; ++i)
                {
                    next = route[i];
                    MatchingSolution.AddAfter(MatchingSolution.Find(prev), next);
                    prev = next;
                }
            }
        }

        /*****
        # A empirical distribution
        def transition_prob(u, v):
            c = 1 / BETA
            # Calculating route distance is expensive.
            # We will discuss how to reduce the number of calls to this function later.
            delta = math.abs(route_distance(u, v) - great_circle_distance(u.measurement, v.measurement))
            return c * math.exp(-delta)
         *****/
        private double TransitionProbability(HiddenMarkovState u, HiddenMarkovState v)
        {
            if (u == s) return 1.0f;
            else if (v == t) return 1.0f;

            Node Source = RoadNetwork.ResolvePoint(u.P, 0.1f).First();
            Node Target = RoadNetwork.ResolvePoint(v.P, 0.1f).First();

            Router router = new Router(Source, Target, string.Empty, 0, TravelMode.Car, ObjFunction.Distance);
            router.Start();

            double routeDistance = router.SolutionCost;//router.Solution.Where(x => x.Id != -1).Sum(x => x.GetDistance(TravelMode.Car));
            //
            double beta = 3;
            double c = 1 / beta;
            double delta = Math.Abs(routeDistance - u.P.DistanceFrom(v.P));
            return c * Math.Exp(-delta);
        }

        private double EdgeCost(HiddenMarkovState u, HiddenMarkovState v)
        {
            return -1 * Math.Log10(TransitionProbability(u, v));
        }

        private double PathProbability(List<Tuple<HiddenMarkovState, HiddenMarkovState>> Path)
        {
            Tuple<HiddenMarkovState, HiddenMarkovState> u_v = Path.First();
            double JointProbability = u_v.Item1.EmissionProbability();
            foreach (Tuple<HiddenMarkovState, HiddenMarkovState> p in Path)
            {
                JointProbability *= TransitionProbability(p.Item1, p.Item2) * p.Item2.EmissionProbability();
            }
            return JointProbability;
        }
    }
}
