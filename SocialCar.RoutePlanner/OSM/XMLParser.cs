using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

using SocialCar.RoutePlanner.Routing;
using SocialCar.RoutePlanner.Routing.Nodes;
using SocialCar.RoutePlanner.Routing.Connections;

namespace SocialCar.RoutePlanner.OSM
{
    public class XMLParser
    {
        private RoutingNetwork Network = null;

        private Dictionary<long, Point> OSMNodeElements = new Dictionary<long, Point>();
        public Dictionary<long, RNode> RNodes = new Dictionary<long, RNode>();

        public void GetBoundaries(string path, ref Point MinPoint, ref Point MaxPoint)
        {
            XmlReader Reader = XmlReader.Create(path);
            bool found = false;

            while ((Reader.Read() && (MinPoint == null) && (MaxPoint == null)))
            {
                if (Reader.NodeType == XmlNodeType.Element)
                {
                    switch (Reader.Name)
                    {
                        case "bounds":
                            MinPoint = new Point(double.Parse(Reader.GetAttribute("minlat")), double.Parse(Reader.GetAttribute("minlon")));
                            MaxPoint = new Point(double.Parse(Reader.GetAttribute("maxlat")), double.Parse(Reader.GetAttribute("maxlon")));
                            break;
                    }
                }
            }
        }

        public void LoadOSMMap(string path, ref RoutingNetwork Network)
        {
            this.Network = Network;
            XmlReader Reader = XmlReader.Create(path);

            while (Reader.Read())
            {
                if (Reader.NodeType == XmlNodeType.Element)
                {
                    switch (Reader.Name)
                    {
                        case "bounds":
                            Network.SetBoundaries(
                                new Point(double.Parse(Reader.GetAttribute("minlat")), double.Parse(Reader.GetAttribute("minlon"))),
                                new Point(double.Parse(Reader.GetAttribute("maxlat")), double.Parse(Reader.GetAttribute("maxlon")))
                            );
                            break;

                        case "node":
                            Point P = new Point(double.Parse(Reader.GetAttribute("lat")), double.Parse(Reader.GetAttribute("lon")));
                            OSMNodeElements.Add(long.Parse(Reader.GetAttribute("id")), P);
                            break;

                        case "way":
                            XElement Way = XElement.Load(Reader.ReadSubtree());
                            // If is a highway
                            if (Way.Elements("tag").Where(t => t.Attribute("k").Value.ToString() == "highway").Count() != 0)
                            {
                                ProcessWay(Way);   
                            }
                            break;

                        default:
                            break;
                    }
                }
                else
                {
                }
            }

            OSMNodeElements.Clear();
        }

        private void ProcessWay(XElement Way)
        {
            LinkedList<RNode> LstWayNodes = new LinkedList<RNode>();
            // Get all nodes forming the way
            IEnumerable<XElement> wayNodes = Way.Elements("nd");
            // Get way tags
            IEnumerable<XElement> wayTags = Way.Elements("tag");
            // For each node in the way
            foreach (XElement wayNode in wayNodes)
            {
                long nodeID = long.Parse(wayNode.Attribute("ref").Value);
                RNode Node = null;
                // If the node doesn't exist create it else retrieve it.
                if (!RNodes.ContainsKey(nodeID))
                {
                    // Get node.
                    Point P = OSMNodeElements[nodeID];
                    double lat = P.Latitude;
                    double lng = P.Longitude;
                    // Create an RNode.
                    Node = Network.AddNode(nodeID, new Point(lat, lng));
                    RNodes.Add(nodeID, Node);
                    LstWayNodes.AddLast(Node);
                }
                else
                {
                    LstWayNodes.AddLast(RNodes[nodeID]);
                }
            }
            //
            Dictionary<string, string> Tags = new Dictionary<string, string>();
            // Get way tags
            foreach (XElement Tag in wayTags)
            {
                string key = Tag.Attribute("k").Value.ToString();
                string value = Tag.Attribute("v").Value.ToString();
                Tags.Add(key, value);
            }
            //
            string oneway = null;
            if (Tags.ContainsKey("oneway"))
                oneway = Tags["oneway"];
            // Create edges.
            RNode u, v;
            for (int i = 1; i < LstWayNodes.Count(); ++i)
            {
                u = LstWayNodes.ElementAt(i - 1);
                v = LstWayNodes.ElementAt(i);
                if (oneway == null || oneway == "no")
                {
                    Network.AddConnection(u, v, true, false).CopyTags(Tags);
                    Network.AddConnection(v, u, true, false).CopyTags(Tags);
                }
                else if (oneway == "yes")
                {
                    Network.AddConnection(u, v, true, false).CopyTags(Tags);
                    Network.AddConnection(v, u, true, true).CopyTags(Tags);
                }
                else if (oneway == "-1")
                {
                    Network.AddConnection(v, u, true, false).CopyTags(Tags);
                    Network.AddConnection(u, v, true, true).CopyTags(Tags);
                }
            }
            LstWayNodes.Clear();
        }
    }
}
