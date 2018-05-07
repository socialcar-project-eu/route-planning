using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using System.IO;
using log4net;
using log4net.Config;
using System.Net;
using SocialCar.RoutePlanner.Routing;
using System.Runtime.Serialization;
using System.ComponentModel;
using SocialCar.RoutePlanner.Carpools;
using SocialCar.RoutePlanner.Traffic;
using SocialCar.RoutePlanner.GTFS;
using SocialCar.RoutePlanner.Routing.Nodes;
using System.Runtime.Serialization.Formatters.Binary;

namespace SocialCar.RoutePlanner.WebAPI
{
    class Program
    {
        public static RoutingNetwork routingNetwork1 = null;
        //public static RoutingNetwork routingNetwork2 = null;
        public static CarpoolParser CParser = null;
        public static TrafficParser TParser = null;
        public static DynamicDataVersion DynamicData = new DynamicDataVersion { };
        public static CarPoolerDataVersioned CarPooling = new CarPoolerDataVersioned { };
        public static TrafficDataVersioned Traffic = new TrafficDataVersioned { };
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        public static int CarpoolingAvailableUpdateTimeFrom = 0;
        public static int CarpoolingAvailableUpdateTimeTo = 2;
        public static int ExtCarpoolingAvailableUpdateTimeFrom = int.Parse(ConfigurationManager.AppSettings["ExtCarpoolingAvailableUpdateTimeFrom"]);
        public static int ExtCarpoolingAvailableUpdateTimeTo = int.Parse(ConfigurationManager.AppSettings["ExtCarpoolingAvailableUpdateTimeTo"]);
        public static string site = ConfigurationManager.AppSettings["site"];

        static void Main(string[] args)
        {
            log.Info("Application starts");

            string host = ConfigurationManager.AppSettings["host"];
            string port = ConfigurationManager.AppSettings["port"];
            string service = ConfigurationManager.AppSettings["service"];
            string protocol = ConfigurationManager.AppSettings["protocol"];
            bool CarpoolingEnabled = bool.Parse(ConfigurationManager.AppSettings["CarpoolingEnabled"]);
            bool TrafficEnabled = bool.Parse(ConfigurationManager.AppSettings["TrafficEnabled"]);
            double TrafficPropagationMaxDistance = float.Parse(ConfigurationManager.AppSettings["TrafficPropagationMaxDistance"]);
            int CarpoolingMaxConnectionsPerTNode = int.Parse(ConfigurationManager.AppSettings["CarpoolingMaxConnectionsPerTNode"]);
            /* This variable is used to decide if update at startup the external carpooling rides to the network or wait the midnight */
            bool updateExtRidesAtStartup = bool.Parse(ConfigurationManager.AppSettings["ExtCarpoolingRideUpdateAtStartup"]);

            //var host = Dns.GetHostAddresses(Dns.GetHostName())[0];
            string url = protocol + "://" + host + ":" + port + service;

            log.Info(url);

            Notification.SendMessageByEmail(Notification.AlarmType.INFO, DateTime.Now.ToString() + "\r\nWebAPI starts at " + url + "\r\nBuilding network...");

            /* Get the site timeZone from the GTFS data (needed for the carpooling date caonversion) */
            string timeZone = null;
            timeZone = DBParser.GetTimeZoneFromAgencyTable();

            /* Get the boundaries from the OSM Map (needed for the backend carpooling http requests) */
            Point MapMinPoint = null, MapMaxPoint = null;
            string OSMXMLMapFilePath = System.IO.Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory + ConfigurationManager.AppSettings["dataPath"] + ConfigurationManager.AppSettings["OSMXMLMapFile"]);
            log.Info("Parsing " + OSMXMLMapFilePath + " OSM.");
            routingNetwork1 = new Routing.RoutingNetwork();
            OSM.XMLParser Parser = new OSM.XMLParser();
            Parser.GetBoundaries(OSMXMLMapFilePath, ref MapMinPoint, ref MapMaxPoint);
            if ( (MapMinPoint != null) && (MapMaxPoint != null) )
                log.Info("Map Boundaries: minlat:" + MapMinPoint.Latitude + ", minlon:" + MapMinPoint.Longitude + ", maxlat:" + MapMaxPoint.Latitude + ", maxlon:" + MapMaxPoint.Longitude);
            else
            {
                string message = "Application ends\r\nMap boundaries not found into the OSM map: minlat:" + MapMinPoint.Latitude + ", minlon:" + MapMinPoint.Longitude + ", maxlat:" + MapMaxPoint.Latitude + ", maxlon:" + MapMaxPoint.Longitude;
                log.Error(message);
                Notification.SendMessageByEmail(Notification.AlarmType.ERROR, DateTime.Now.ToString() + "\r\n" + message);
                Environment.Exit(0);
            }

            
            /* Get the dynamic data version from the backend web service */
            DynamicData = HTTPRequests.getDynamicDataVersionFromBackend(MapMinPoint, MapMaxPoint);

            if (DynamicData != null)
            {
                if (CarpoolingEnabled)
                {
                    /* Set the Carpooling data version */
                    CarPooling.Version = new CarpoolerVersion(DynamicData.sites.First().carpooling_info.version,
                        DynamicData.sites.First().carpooling_info.updated,
                        DynamicData.sites.First().name,
                        DynamicData.sites.First().carpooling_info.nightly_version,
                        DynamicData.sites.First().carpooling_info.nightly_updated);
                    log.Info("CARPOOLING Version:" + CarPooling.Version.version + " Timestamp:" + CarPooling.Version.timestampVersion +
                        " NightlyVersion:" + CarPooling.Version.nightly_version + " NightlyTimestamp: " + CarPooling.Version.nightly_timestampVersion);

                    bool updateExternalRides = false;
                    if ((updateExtRidesAtStartup) || ((DateTime.Now.Hour < ExtCarpoolingAvailableUpdateTimeFrom) && (DateTime.Now.Hour >= ExtCarpoolingAvailableUpdateTimeTo)) )
                        updateExternalRides = true;

                    /* Get the carpooling rides list from the backend web service */
                    CarPooling.Carpoolers = HTTPRequests.getCarpoolingDataFromBackend(MapMinPoint, MapMaxPoint, updateExternalRides);
                }

                if (TrafficEnabled)
                {
                    /* Set the Traffic/incidents data version */
                    Traffic.Version = new TrafficVersion(DynamicData.sites.First().reports_info.version,
                    DynamicData.sites.First().reports_info.updated,
                    DynamicData.sites.First().name);
                    log.Info("TRAFFIC Version:" + Traffic.Version.version + " Timestamp:" + Traffic.Version.timestampVersion);

                    /* Get the Traffic/incidents data from the backend web service */
                    Traffic.TrafficReport = HTTPRequests.getTrafficDataFromBackend(MapMinPoint, MapMaxPoint, TrafficPropagationMaxDistance);
                }
            }

            /* Build the network */
            routingNetwork1 = RoutePlanner.Globals.BuildNetwork(ref CParser, ref TParser, CarPooling.Carpoolers, Traffic.TrafficReport, TrafficPropagationMaxDistance, CarpoolingMaxConnectionsPerTNode);

            /* Copy the network */
            //routingNetwork2 = new RoutingNetwork { };
            //routingNetwork1.duplicateNetwork(ref routingNetwork2);

            log.Info("Starting server at " + url);
            Notification.SendMessageByEmail(Notification.AlarmType.INFO, DateTime.Now.ToString() + "\r\nService is running at " + url);
            HTTPAsyncServer Server = new HTTPAsyncServer(url);
            
            log.Error("Application ends");
            Notification.SendMessageByEmail(Notification.AlarmType.ERROR, "Application ends");
        }

    }
}