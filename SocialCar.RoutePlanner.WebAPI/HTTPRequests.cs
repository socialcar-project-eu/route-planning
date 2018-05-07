using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SocialCar.RoutePlanner.Carpools;
using SocialCar.RoutePlanner.Traffic;
using log4net;
using log4net.Config;
using System.Net;
using System.IO;
using Newtonsoft.Json;
using SocialCar.RoutePlanner.Routing.Nodes;

namespace SocialCar.RoutePlanner.WebAPI
{
    public class DynamicDataVersion
    {
        public List<Site> sites { get; set; }

        public class Site
        {
            public string url { get; set; }
            public ReportsInfo reports_info { get; set; }
            public string _id { get; set; }
            public CarpoolingInfo carpooling_info { get; set; }
            public BoundingBox bounding_box { get; set; }
            public string name { get; set; }
        }

        public class ReportsInfo
        {
            public int version { get; set; }
            public int updated { get; set; }
        }

        public class CarpoolingInfo
        {
            public int version { get; set; }
            public int updated { get; set; }
            public int nightly_version { get; set; }
            public int nightly_updated { get; set; }
        }

        public class BoundingBox
        {
            public double min_lon { get; set; }
            public double min_lat { get; set; }
            public double max_lat { get; set; }
            public double max_lon { get; set; }
        }

    }




    public class HTTPRequests
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /* Get the version of the carpooling rides */
        private static readonly string sites_boundary = "https://xxx.xx/xxx1";

        /* Get the carpooling rides */
        private static readonly string rides_boundary = "https://xxx.xx/xxx2";

        /* Get traffic/incidents information */
        private static readonly string reports_boundary = "https://xxx.xx/xxx3";

        private static readonly string user = ""; /* USERNAME */
        private static readonly string pw = ""; /* PW */

        public static List<Carpooler> getCarpoolingDataFromBackend(Routing.Nodes.Point MinPoint, Point MaxPoint, bool updateExternalRides)
        {
            /* Set this to true if you want to use real waypoints instead of intermediate ptstops, remember to also change the use_waypoints value in the CarpoolParser.cs file */
            bool use_waypoints = false;

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls
               | SecurityProtocolType.Tls11
               | SecurityProtocolType.Tls12
               | SecurityProtocolType.Ssl3;
            List<Carpooler> CarpoolRides = new List<Carpooler> { };
            CarpoolerJson Carpooler = new CarpoolerJson { };

            string url = rides_boundary + "?" +
                "min_lat=" + MinPoint.Latitude + "&" +
                "min_lon=" + MinPoint.Longitude + "&" +
                "max_lat=" + MaxPoint.Latitude + "&" +
                "max_lon=" + MaxPoint.Longitude + "&" +
                "site=" + Program.site;

            try
            {
                HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
                httpWebRequest.Method = WebRequestMethods.Http.Get;
                httpWebRequest.Accept = "application/json";
                httpWebRequest.Credentials = new NetworkCredential(user, pw);
                httpWebRequest.UserAgent = "RP client";

                HttpWebResponse response = (HttpWebResponse)httpWebRequest.GetResponse();
                log.Info(url);
                if (response != null && response.StatusCode == HttpStatusCode.OK)
                {
                    Stream stream = response.GetResponseStream();
                    StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                    String responseString = reader.ReadToEnd();

                    Carpooler = JsonConvert.DeserializeObject<CarpoolerJson>(responseString);

                    if ( (Carpooler == null) || (Carpooler.rides.Count == 0) )
                    {
                        log.Warn("Carpooling: no data available from backend");
                    }
                    else
                    {
                        int numExtRidesIgnored = 0;

                        foreach (CarpoolerJson.Ride el in Carpooler.rides)
                        {
                            // Just for testing if the carpooling rides are expired
                            if (el.date < Program.DynamicData.sites.First().carpooling_info.nightly_updated)
                                log.Error("Ride should be expired but it is present el.date:" + el.date + " nightly_timestampVersion:" + Program.DynamicData.sites.First().carpooling_info.nightly_updated);

                            //!!!DEBUG EDIMBURGO!!!
                            //if ((String.Compare(el._id, "5976e793e04bd56a9a84ffa8") == 0) ||
                            //    (String.Compare(el._id, "1dfd2cd0faebcf1b768a77bd") == 0) )
                            //{

                            //!!!DEBUG TICINO!!!
                            //if ((String.Compare(el.name, "Test Milano Lugano 01") == 0) ||
                            //    (String.Compare(el.name, "Test Biella Lugano 01") == 0) ||
                            //    (String.Compare(el.name, "Test Cavaglietto Lugano 01") == 0) ||
                            //    (String.Compare(el.name, "Test Biasca Lugano 01") == 0) ||
                            //    (String.Compare(el.name, "Test Airolo Lugano 01") == 0) ||
                            //    (String.Compare(el.name, "Test Bergamo Lugano 01") == 0) ||
                            //     (String.Compare(el._id, "5d85a82ea07e1160a5669e29") == 0))
                            //{

                            IEnumerable<Routing.Nodes.Point> Points = GooglePoints.Decode(el.polyline.Replace(@"\\", @"\"));
                            //List<Routing.Nodes.Point> Points = GooglePoints.DecodePolylinePoints(el.polyline.Replace(@"\\", @"\"));
                            //List<Routing.Nodes.Point> Points = GooglePoints.decodePoly(el.polyline);

                            //string test = GooglePoints.DecodeLocations(el.polyline.Replace(@"\\", @"\"));
                            //foreach (Point p in Points)
                            //log.Info(p.Latitude + ", " + p.Longitude);

                            string startDate = Globals.GetDateFromTimestamp(el.date);
                            
                                SourceCPRide provider = SourceCPRide.Unknown;
                                if (el.extras != null)
                                    provider = SourceCPRide.External;
                                else
                                    provider = SourceCPRide.SocialCar;

                                if ( (provider == SourceCPRide.SocialCar) || 
                                        ((provider == SourceCPRide.External) && (updateExternalRides == true)) )
                                {
                                    Carpooler Pooler = new Carpooler(el._id, el.name, Globals.GetLocalTimeSinceMidnight(el.date), int.MaxValue, el.activated, startDate, provider);
                                
                                    /* Add start point to the ride */
                                    Pooler.WayPointsOrig.Add(new Point(el.start_point.lat, el.start_point.lon));
                                    Pooler.WayPointsUsed.Add(new Point(el.start_point.lat, el.start_point.lon));

                                    /* Add all waypoints to the ride (THIS DOES NOT WORK SINCE THE WAYPOINTS DON'T MATCH WITH OUR NETWORK )*/
                                    if (use_waypoints == true)
                                    {
                                        //Pooler.WayPointsTmpList.Add(new Point(el.start_point.lat, el.start_point.lon));
                                        //foreach (Point p in Points)
                                        //{
                                        //    Pooler.WayPointsOrig.Add(p);
                                        //    Pooler.WayPointsUsed.Add(p);
                                        //}
                                    }

                                    /* Add end point to the ride */
                                    Pooler.WayPointsUsed.Add(new Point(el.end_point.lat, el.end_point.lon));
                                    Pooler.WayPointsOrig.Add(new Point(el.end_point.lat, el.end_point.lon));

                                    CarpoolRides.Add(Pooler);
                                }
                                else
                                {
                                    if (updateExternalRides == false)
                                        numExtRidesIgnored++;
                                }
                            //}
                        }
                        if (updateExternalRides == false)
                            log.Info("updateExternalRides:" + updateExternalRides + " Ignored:" + numExtRidesIgnored + " external rides");
                    }
                }
                else
                {
                    log.Error("An error occured while calling the carpooling data service: StatusCode=" + response.StatusCode);
                    return null;
                }
            }
            catch (Exception ex)
            {
                log.Error("An error occured during the carpooling data get: " + ex.ToString());
                return null;
            }

            if (CarpoolRides != null)
            {
                if (CarpoolRides.Count == 0)
                    log.Warn(CarpoolRides.Count + " carpooling rides received");
                else
                    log.Info(CarpoolRides.Count + " carpooling rides received");
            }

            return CarpoolRides;
        }

        public static List<TrafficReport> getTrafficDataFromBackend(Routing.Nodes.Point MinPoint, Point MaxPoint, double TrafficPropagationMaxDistance)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls
               | SecurityProtocolType.Tls11
               | SecurityProtocolType.Tls12
               | SecurityProtocolType.Ssl3;
            List<TrafficReport> TrafficReport = new List<TrafficReport> { };
            TrafficJson TrafficJson = new TrafficJson { };

            string url = reports_boundary + "?" +
                "min_lat=" + MinPoint.Latitude + "&" +
                "min_lon=" + MinPoint.Longitude + "&" +
                "max_lat=" + MaxPoint.Latitude + "&" +
                "max_lon=" + MaxPoint.Longitude;

            try
            {
                HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
                httpWebRequest.Method = WebRequestMethods.Http.Get;
                httpWebRequest.Accept = "application/json";
                httpWebRequest.Credentials = new NetworkCredential(user, pw);
                httpWebRequest.UserAgent = "RP client";

                HttpWebResponse response = (HttpWebResponse)httpWebRequest.GetResponse();
                log.Info(url);
                if (response != null && response.StatusCode == HttpStatusCode.OK)
                {
                    Stream stream = response.GetResponseStream();
                    StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                    String responseString = reader.ReadToEnd();

                    TrafficJson = JsonConvert.DeserializeObject<TrafficJson>(responseString);

                    ///* ONLY FOR TESTING: add one fake traffic report in "Massagno, Praccio" */
                    //// Accident in "Massagno Praccio"
                    //TrafficJson.Report testReport = new TrafficJson.Report();
                    //TrafficJson.reports = new List<TrafficJson.Report>();
                    //testReport.category = "ACCIDENT";
                    //testReport.severity = "HIGH";
                    //testReport._id = "000000000000000000000001";
                    //testReport.loc = new TrafficJson.Loc();
                    //testReport.loc.coordinates = new List<double> { 8.943813, 46.01345 };
                    //testReport.loc.type = "Point";
                    //TrafficJson.reports.Add(testReport);
                    ///* END TESTING */

                    ///* ONLY FOR TESTING: add one fake traffic report in "Chiasso" */
                    //// Accident in "Chiasso"
                    //TrafficJson.Report testReport = new TrafficJson.Report();
                    //TrafficJson.reports = new List<TrafficJson.Report>();
                    //testReport.category = "ACCIDENT";
                    //testReport.severity = "MEDIUM";
                    //testReport._id = "000000000000000000000001";
                    //testReport.loc = new TrafficJson.Loc();
                    //testReport.loc.coordinates = new List<double> { 9.022495, 45.839520 };
                    //testReport.loc.type = "Point";
                    //TrafficJson.reports.Add(testReport);
                    ///* END TESTING */

                    if ((TrafficJson == null) || (TrafficJson.reports.Count == 0))
                    {
                        log.Warn("Traffic: no data available from backend");
                    }
                    else
                    { 
                        foreach (TrafficJson.Report el in TrafficJson.reports)
                        {
                            TrafficReport Traffic = new TrafficReport(el._id, new Point(el.location.geometry.coordinates.Last(), el.location.geometry.coordinates.First()), el.category, el.severity, el.location.address, TrafficPropagationMaxDistance);
                            TrafficReport.Add(Traffic);
                        }
                    }
                }
                else
                {
                    log.Error("An error occured while calling the traffic data service: StatusCode=" + response.StatusCode);
                    return null;
                }
            }
            catch (Exception ex)
            {
                log.Error("An error occured during the traffic data get: " + ex.ToString());
                return null;
            }

            if (TrafficReport != null)
            {
                if (TrafficReport.Count == 0)
                    log.Warn(TrafficReport.Count + " traffic reports received");
                else
                    log.Info(TrafficReport.Count + " traffic reports received");
            }

            return TrafficReport;
        }

        /*
         * DEPRECATED:  this function was used when the sites_boundary backend sercice just replied with the carpooling data vesion.
         *              Then the backend added into the same service the version of all dynamic services.
         *              After implementing the "getDynamicDataVersionFromBackend" function, this should no longer be used.
         */
        //public static CarpoolerVersion getCarpoolingDataVersionFromBackend(Point MinPoint, Point MaxPoint)
        //{
        //    CarpoolerVersion version = null;
        //    CarpoolerVersionJson CarpoolerVersionJson = new CarpoolerVersionJson { }; 

        //    string url = sites_boundary + "?" +
        //        "min_lat=" + MinPoint.Latitude + "&" +
        //        "min_lon=" + MinPoint.Longitude + "&" +
        //        "max_lat=" + MaxPoint.Latitude + "&" +
        //        "max_lon=" + MaxPoint.Longitude;

        //    try
        //    {
        //        HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
        //        httpWebRequest.Method = WebRequestMethods.Http.Get;
        //        httpWebRequest.Accept = "application/json";
        //        httpWebRequest.Credentials = new NetworkCredential(user, pw);
        //        httpWebRequest.UserAgent = "RP client";

        //        HttpWebResponse response = (HttpWebResponse)httpWebRequest.GetResponse();
        //        log.Info(url);
        //        if (response != null && response.StatusCode == HttpStatusCode.OK)
        //        {
        //            Stream stream = response.GetResponseStream();
        //            StreamReader reader = new StreamReader(stream, Encoding.UTF8);
        //            String responseString = reader.ReadToEnd();

        //            CarpoolerVersionJson = JsonConvert.DeserializeObject<CarpoolerVersionJson>(responseString);

        //            version = new CarpoolerVersion(CarpoolerVersionJson.sites.First().carpooling_info.version, 
        //                CarpoolerVersionJson.sites.First().carpooling_info.updated, 
        //                CarpoolerVersionJson.sites.First().name);
        //            log.Info("Version: " + version.version + " Timestamp: " + version.timestampVersion);
        //        }
        //        else
        //        {
        //            log.Error("An error occured while calling the carpooling data service: StatusCode=" + response.StatusCode);
        //            return null;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        log.Error("An error occured during the carpooling data get: " + ex.ToString());
        //        return null;
        //    }

        //    return version;
        //}


        public static DynamicDataVersion getDynamicDataVersionFromBackend(Point MinPoint, Point MaxPoint)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls
               | SecurityProtocolType.Tls11
               | SecurityProtocolType.Tls12
               | SecurityProtocolType.Ssl3;
            DynamicDataVersion DynamicData = new DynamicDataVersion { };

            string url = sites_boundary + "?" +
                "min_lat=" + MinPoint.Latitude + "&" +
                "min_lon=" + MinPoint.Longitude + "&" +
                "max_lat=" + MaxPoint.Latitude + "&" +
                "max_lon=" + MaxPoint.Longitude;

            try
            {
                HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
                httpWebRequest.Method = WebRequestMethods.Http.Get;
                httpWebRequest.Accept = "application/json";
                httpWebRequest.Credentials = new NetworkCredential(user, pw);
                httpWebRequest.UserAgent = "RP client";

                //log.Info(url);
                HttpWebResponse response = (HttpWebResponse)httpWebRequest.GetResponse();
                
                if (response != null && response.StatusCode == HttpStatusCode.OK)
                {
                    Stream stream = response.GetResponseStream();
                    StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                    String responseString = reader.ReadToEnd();

                    DynamicData = JsonConvert.DeserializeObject<DynamicDataVersion>(responseString);
                }
                else
                {
                    log.Error("An error occured while calling the dynamic data version service: StatusCode=" + response.StatusCode);
                    return null;
                }
            }
            catch (Exception ex)
            {
                log.Warn("An issue occured during the dynamic data version get: " + ex.ToString());
                return null;
            }

            return DynamicData;
        }
    }
}
