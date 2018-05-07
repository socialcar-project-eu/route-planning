using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SqlClient;
using System.Configuration;
using System.IO;
using SocialCar.RoutePlanner.Routing.Nodes;
using SocialCar.RoutePlanner.GTFS;
using log4net;
using log4net.Config;
using SocialCar.RoutePlanner.Carpools;
using SocialCar.RoutePlanner.Traffic;

namespace SocialCar.RoutePlanner
{
    public static class Globals
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// A utility to convert string time values in the form "HH:MM:SS (or HH:MM)
        /// to seconds since midnight. Since the GTFS database does not run on a 24 hour clock (some trips
        /// may extend past midnight into the "24th" hour) storing arrival and departure times as DateTime objects
        /// is not possible. So they may either be stored as strings "HH:MM:SS" or as integers.
        /// </summary>
        /// <param name="time">string in the form "HH:MM:SS" 24-h format</param>
        /// <returns>time as an integer, seconds since midnight</returns>
        public static int ConvertTimeToSeconds(string time)
        {
            int secSinceMidnight = 0;
            int hours, minutes, seconds;
            // split the a_time string based on the semicolons
            string[] splitTime = time.Split(':');

            hours = Int32.Parse(splitTime[0]);
            minutes = Int32.Parse(splitTime[1]);
            if (splitTime.Count() == 3)
                seconds = Int32.Parse(splitTime[2]);
            else
                seconds = 0;

            secSinceMidnight = (3600 * hours) + (60 * minutes) + seconds;

            return secSinceMidnight;
        }


        ///Used to convert date and time strings in the site Local DateTime format
        ///<param name="date">string in the form "YYYYMMDD" format</param>
        ///<param name="time">string in the form "HH:MM:SS" 24-h format</param>
        /// <returns>time as a DateTime
        public static DateTime ConvertDateAndTimeToLocalSiteDateTime(string date, string time)
        {
            
            int years, months, days, hours, minutes, seconds;
            // split the a_time string based on the semicolons
            string[] splitTime = time.Split(':');
            years = Int32.Parse(date.Substring(0, 4));
            months = Int32.Parse(date.Substring(4, 2));
            days = Int32.Parse(date.Substring(6, 2));
            hours = Int32.Parse(splitTime[0]);
            minutes = Int32.Parse(splitTime[1]);
            seconds = Int32.Parse(splitTime[2]);


            DateTime DT = new DateTime(years, months, days, hours, minutes, seconds, DateTimeKind.Utc);

            //DT = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DT, TimeZoneInfo.Local.Id, DBParser.timeZone);
            DT = TimeZoneInfo.ConvertTimeFromUtc(DT, TimeZoneInfo.FindSystemTimeZoneById(DBParser.timeZone));

            return DT;
        }

        public static double GetDistanceFromLine(Point P0, Point P1, Point P2)
        {
            double x0 = P0.Latitude, y0 = P0.Longitude;
            double x1 = P1.Latitude, y1 = P1.Longitude;
            double x2 = P2.Latitude, y2 = P2.Longitude;

            double YX0 = (y2 - y1) * x0;
            double XY0 = (x2 - x1) * y0;
            double x2y1 = x2 * y1;
            double y2x1 = y2 * x1;
            double y2y1 = Math.Pow(y2 - y1, 2);
            double x2x1 = Math.Pow(x2 - x1, 2);

            return Math.Abs(YX0 - XY0 + x2y1 - y2x1) / Math.Sqrt(y2y1 + x2x1);
        }

        public static int GetUnixTimeStamp(DateTime D)
        {
            int unixDateTime;

            D = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(D, DBParser.timeZone, TimeZoneInfo.Utc.Id);
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            unixDateTime = (int)(D.ToUniversalTime() - epoch).TotalSeconds;

            return unixDateTime;
        }

        public static int GetUnixTimeStampOutMsg(DateTime D)
        {
            int unixDateTime;

            D = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(D, TimeZoneInfo.Local.Id, DBParser.timeZone);
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            unixDateTime = (int)(D.ToUniversalTime() - epoch).TotalSeconds;

            return unixDateTime;
        }
        
        public static int GetLocalTimeSinceMidnight(long unixTimeStamp)
        {
            int secSinceMidnight;

            DateTime D = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            D = D.AddSeconds(unixTimeStamp).ToUniversalTime();
            TimeZoneInfo cstZone = TimeZoneInfo.FindSystemTimeZoneById(DBParser.timeZone);
            DateTime DtimeZone = TimeZoneInfo.ConvertTimeFromUtc(D, cstZone);

            // Unix timestamp is seconds past epoch
            secSinceMidnight = (DtimeZone.Hour * 3600) + (DtimeZone.Minute * 60) + DtimeZone.Second;

            return secSinceMidnight;
        }

        public static string GetDateFromTimestamp (long timestamp)
        {
            string startDate = "";

            DateTime D = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

            var dt = new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(timestamp).ToUniversalTime();
            startDate = dt.ToString("yyyyMMdd");

            return startDate;
        }

        public static Routing.RoutingNetwork BuildNetwork(ref Carpools.CarpoolParser CParser, ref Traffic.TrafficParser TParser, List<Carpooler> CarPoolers, List<TrafficReport> TrafficReport, double TrafficPropagationMaxDistance, int CarpoolingMaxConnectionsPerTNode)
        {
            Routing.RoutingNetwork routingNetwork = null;

            string Path = System.IO.Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory + ConfigurationManager.AppSettings["dataPath"] + "\\" + ConfigurationManager.AppSettings["NetworkFileName"]);

            if (!File.Exists(Path))
            {
                log.Info("Routing network bin file not exist: " + Path + " The road network will be generated from the osm map");
                
                // Construct the road network.
                string OSMXMLMapFilePath = System.IO.Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory + ConfigurationManager.AppSettings["dataPath"] + ConfigurationManager.AppSettings["OSMXMLMapFile"]);
                log.Info("Parsing " + OSMXMLMapFilePath + " OSM.");

                routingNetwork = new Routing.RoutingNetwork();
                OSM.XMLParser Parser = new OSM.XMLParser();
                Parser.LoadOSMMap(OSMXMLMapFilePath, ref routingNetwork);
                
                // Construct the transportation network.
                log.Info("Parsing GTFS.");

                GTFS.DBParser GTFSParser = new GTFS.DBParser();

                if (CarPoolers != null)
                    routingNetwork.CarpoolingMaxConnectionsPerTNode = CarpoolingMaxConnectionsPerTNode;

                GTFSParser.LoadTransportationNetwork(ref routingNetwork);
                log.Info("Attaching GTFS.");

                routingNetwork.ConnectTransportation();

                /* Add data to the Stops Carpooling connections */
                routingNetwork.CreateNetworkForCarpoolingRides();

                //routingNetwork.Serialize();
            }
            else
            {
                log.Info("Routing network: " + Path);
                log.Info("DeSrializing Netwrok.");
                routingNetwork = Routing.RoutingNetwork.DeSerialize();
            }

            /* Construct Carpooling network */
            if (CarPoolers != null)
            {
                log.Info("Constructing Carpooling network (" + CarPoolers.Count + " rides)");
                CParser = new CarpoolParser(routingNetwork, CarPoolers);
                //CParser.BuildCarpoolRoutesFromXML();
                //CParser.BuildCarpoolRoutesFromJson();
                CParser.ConnectWithRoadNetwork();
            }

            /* Update the network considering the traffic reports */
            if (TrafficReport != null)
            {
                routingNetwork.TrafficPropagationMaxDistance = TrafficPropagationMaxDistance;
                log.Info("Updating the network considering the traffic reports (" + TrafficReport.Count + " reports)");
                TParser = new TrafficParser(routingNetwork, TrafficReport);
                TParser.UpdateNetworkWithTrafficReport();
            }

            return routingNetwork;
        }
    }

    public class DBConnection
    {
        private string _connectionString;
        public readonly SqlConnection Connection;

        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public DBConnection()
        {
            string serverName = "";
            string DBInstanceName = ConfigurationManager.AppSettings["DBInstanceName"];
            if (DBInstanceName != null)
                serverName = Environment.MachineName + "\\" + DBInstanceName;
            else
                serverName = Environment.MachineName + "\\SQLEXPRESS";
            
            string DBName = ConfigurationManager.AppSettings["DBName"];
            log.Info("DB server: " + serverName);
            log.Info("DB name: " + DBName);

            _connectionString = "Data Source=" + serverName + ";Initial Catalog=" + DBName + ";" +
            "Integrated Security=true; MultipleActiveResultSets=True;";

            Connection = new SqlConnection(_connectionString);
            if (Connection.State == System.Data.ConnectionState.Closed)
                Connection.Open();
        }

        public System.Data.ConnectionState State()
        {
            return Connection.State;
        }

        public bool OpenConnection()
        {
            try
            {
                if (Connection.State == System.Data.ConnectionState.Closed)
                    Connection.Open();
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return true;
        }

        public bool CloseConnection()
        {
            try
            {
                if (Connection.State == System.Data.ConnectionState.Open)
                    Connection.Close();
            }
            catch (Exception ex)
            {
               throw ex;
            }
            return true;
        }
    }
}
