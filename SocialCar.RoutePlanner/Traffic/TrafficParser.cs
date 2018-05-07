using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net.Config;
using SocialCar.RoutePlanner.Routing;
using log4net;
using System.Configuration;

namespace SocialCar.RoutePlanner.Traffic
{
    public class TrafficParser
    {
        public List<TrafficReport> TrafficReport = new List<TrafficReport>();
        private RoutingNetwork RoadNetwork;
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public TrafficParser(RoutingNetwork network, List<TrafficReport> TrafficReport)
        {
            RoadNetwork = network;
            this.TrafficReport = TrafficReport;
        }

        /* Update the network given the reports related the traffic conditions */
        public void UpdateNetworkWithTrafficReport()
        {
            foreach (TrafficReport T in TrafficReport)
            {
                RoadNetwork.AddTrafficReport(T);
            }

        }

        public static TrafficDiff checkDifferences(List<TrafficReport> oldList, List<TrafficReport> newList)
        {
            TrafficDiff Diff = new TrafficDiff { };

            foreach (TrafficReport elNew in newList)
            {
                int index = -1;
                index = oldList.FindIndex(x => x.Id == elNew.Id);

                /* Check if the element is exactly the same, in case ignore it and remove from the old list */
                if (index != -1)
                {
                    if ( (oldList[index].Id == elNew.Id) &&
                         (oldList[index].Coordinates.Latitude == elNew.Coordinates.Latitude) &&
                         (oldList[index].Coordinates.Longitude == elNew.Coordinates.Longitude) &&
                         (oldList[index].Category == elNew.Category) &&
                         (oldList[index].Severity == elNew.Severity) &&
                         (oldList[index].timestamp == elNew.timestamp) )
                    {
                        /* The traffic report already exists and it is not changed (nothing to do in the network, we can add it to the ignore list) */
                        Diff.ElementsToIgnore.Add(elNew);
                    }
                    else
                    {
                        /* The traffic report already exists but it is changed (so we need to remove it from the network and add the new one) */
                        Diff.ElementsToAdd.Add(elNew);
                        Diff.ElementsToRemove.Add(oldList[index]);
                    }
                }
                else
                {
                    /* The traffic report does not exist (so we need to add it to the network) */
                    Diff.ElementsToAdd.Add(elNew);
                }
            }


            /* Add all the elements not included in "Added" or "Removed" into the "Removed" list */
            foreach (TrafficReport elOld in oldList)
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

            return Diff;
        }
    }
    

}
