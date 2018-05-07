using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SqlClient;    
using SocialCar.RoutePlanner.Routing.Nodes;
using SocialCar.RoutePlanner.Routing.Connections;
using SocialCar.RoutePlanner.Routing;
using log4net;
using log4net.Config;
using NodaTime;
using NodaTime.TimeZones;
using System.Globalization;

namespace SocialCar.RoutePlanner.GTFS
{
    public class DBParser
    {
        public SortedDictionary<string, TNode> Stops { get; private set; }
        public static string timeZone = null;
        private static DBConnection DBConnection = new DBConnection();
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public void LoadTransportationNetwork(ref RoutingNetwork Network)
        {
            timeZone = GetTimeZoneFromAgencyTable();

            ///* Add agency table */
            //CreateAgencyTable(Network);

            if (timeZone != null)
            {
                CreateStops(Network.MinPoint, Network.MaxPoint, ref Network);
                GetTimetableData(ref Network, Network.MinPoint, Network.MaxPoint);
            } else
            {
                log.Error("Time zone error, check the agency file");
                Environment.Exit(-1);
            }

        }

        //public static void CreateAgencyTable(RoutingNetwork Network)
        //{
        //    if (DBConnection.OpenConnection())
        //    {
        //        SqlCommand cmd = new SqlCommand();
        //        SqlDataReader reader;

        //        cmd.Connection = DBConnection.Connection;
        //        cmd.CommandText = "SELECT * " +
        //                          "FROM agency ";
        //        cmd.CommandType = System.Data.CommandType.Text;
        //        reader = cmd.ExecuteReader();

        //        while (reader.Read())
        //        {
        //            Network.addAgency(reader["agency_id"].ToString(), reader["agency_name"].ToString());
        //        }

        //        reader.Close();
        //    }
        //}

        public static string GetTimeZoneFromAgencyTable()
        {
            List<string> timeZoneList = new List<string> { };

            if (DBConnection.OpenConnection())
            {
                SqlCommand cmd = new SqlCommand();
                SqlDataReader reader;
                
                cmd.Connection = DBConnection.Connection;
                cmd.CommandText = "SELECT * " +
                                  "FROM agency ";
                cmd.CommandType = System.Data.CommandType.Text;
                reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    string timezoneId = reader["agency_timezone"].ToString();
                    if (!timeZoneList.Contains(timezoneId))
                        timeZoneList.Add(timezoneId);
                }

                reader.Close();

                if (timeZoneList.Count == 1)
                {
                    var mappings = TzdbDateTimeZoneSource.Default.WindowsMapping.MapZones;
                    var map = mappings.FirstOrDefault(x => x.TzdbIds.Any(z => z.Equals(timeZoneList[0], StringComparison.OrdinalIgnoreCase)));
                    timeZone = TimeZoneInfo.FindSystemTimeZoneById(map.WindowsId).Id;
                    log.Info("Using timezone: " + timeZoneList[0] + " (" + timeZone + ")");

                } else if (timeZoneList.Count == 0)
                {
                    log.Error("Missing time zone");
                }
                else { 
                    log.Error("Multiple time zones");
                    foreach (string tz in timeZoneList)
                        log.Error("TimeZoneList element: " + tz);
                }
            }

            return timeZone;
        }

        private void CreateStops(Point MinPoint, Point MaxPoint, ref RoutingNetwork Network)
        {
            Stops = new SortedDictionary<string, TNode>();

            if (DBConnection.OpenConnection())
            {
                SqlCommand cmd = new SqlCommand();
                SqlDataReader reader;
                //
                cmd.Connection = DBConnection.Connection;
                cmd.CommandText = "SELECT * " +
                                  "FROM stops " +
                                  "WHERE stop_lat >= " + MinPoint.Latitude.ToString() + " AND " +
                                  "stop_lon >= " + MinPoint.Longitude.ToString() + " AND " +
                                  "stop_lat <= " + MaxPoint.Latitude.ToString() + " AND " +
                                  "stop_lon <= " + MaxPoint.Longitude.ToString();
                cmd.CommandType = System.Data.CommandType.Text;
                reader = cmd.ExecuteReader();
                //
                TNode Stop = null;
                long nextStopId = 1;
                while (reader.Read())
                {
                    Stop = Network.AddNode(nextStopId,
                        new Point(
                        double.Parse(reader["stop_lat"].ToString()),
                        double.Parse(reader["stop_lon"].ToString())
                        ), reader["stop_id"].ToString(), reader["stop_name"].ToString());
                    //
                    Stop.StopCode = reader["stop_code"].ToString();
                    //
                    Stops.Add(Stop.StopId, Stop);
                    ++nextStopId;
                }

                reader.Close();
            }
        }

        private void GetTimetableData(ref RoutingNetwork Network, Point MinPoint, Point MaxPoint)
        {
            if (DBConnection.OpenConnection())
            {
                SqlCommand cmd = new SqlCommand();
                SqlCommand cmd2 = new SqlCommand();
                SqlDataReader reader;
                cmd.Connection = DBConnection.Connection;
                cmd2.Connection = DBConnection.Connection;
                /*
                 * This query constructs for every route and trip the direct connections
                 * from one station to the next. The arrival and departure time of the source station
                 * with the stop sequence included, and the same for the destination station.
                 */
                cmd.CommandText = "SELECT T1.route_id, route_long_name, route_short_name, route_desc, route_type, route_url, agency_id, T1.trip_id, T1.service_id," +
                                    "T1.stop_id, T1.arrival_time, T1.departure_time, T1.stop_sequence, " +
                                    "T2.stop_id AS 'next_stop_id', T2.arrival_time AS 'next_arrival_time', " +
                                    "T2.departure_time AS 'next_departure_time', T2.stop_sequence AS 'next_stop_sequence' " +
                                    "FROM " +
                                    "( " +
                                        "SELECT trips.route_id, stop_times.trip_id, arrival_time, departure_time, stop_id, stop_sequence, trips.service_id " +
                                        "FROM trips " +
                                        "INNER JOIN stop_times ON stop_times.trip_id = trips.trip_id " +
                                        "WHERE stop_id in ( " +
					                                "SELECT stop_id " +
					                                "FROM stops " +
					                                "WHERE (stop_lat BETWEEN @min_lat AND @max_lat) AND (stop_lon BETWEEN @min_lon AND @max_lon) " +
				                                ") " +
                                    ") AS T1 " +
                                    "INNER JOIN " +
                                    "(  " +
                                        "SELECT trips.route_id, stop_times.trip_id, arrival_time, departure_time, " +
                                        "stop_id, stop_sequence  " +
                                        "FROM trips  " +
                                        "INNER JOIN stop_times ON stop_times.trip_id = trips.trip_id " +
                                        "WHERE stop_id in ( " +
					                        "SELECT stop_id " +
					                        "FROM stops " +
					                        "WHERE (stop_lat BETWEEN @min_lat AND @max_lat) AND (stop_lon BETWEEN @min_lon AND @max_lon) " +
				                        ") " +
                                    ") AS T2 " +
                                    "ON T1.route_id = T2.route_id AND T1.trip_id = T2.trip_id AND T2.stop_sequence = T1.stop_sequence + 1 " +
                                    "INNER JOIN routes ON T1.route_id = routes.route_id";
                //
                cmd.Parameters.Add(new SqlParameter("@min_lat", System.Data.SqlDbType.Real)).Value = MinPoint.Latitude;
                cmd.Parameters.Add(new SqlParameter("@max_lat", System.Data.SqlDbType.Real)).Value = MaxPoint.Latitude;
                cmd.Parameters.Add(new SqlParameter("@min_lon", System.Data.SqlDbType.Real)).Value = MinPoint.Longitude;
                cmd.Parameters.Add(new SqlParameter("@max_lon", System.Data.SqlDbType.Real)).Value = MaxPoint.Longitude;
                //
                cmd2.CommandText = "select count(*) from (" + cmd.CommandText + ") as tab";
                cmd2.Parameters.Add(new SqlParameter("@min_lat", System.Data.SqlDbType.Real)).Value = MinPoint.Latitude;
                cmd2.Parameters.Add(new SqlParameter("@max_lat", System.Data.SqlDbType.Real)).Value = MaxPoint.Latitude;
                cmd2.Parameters.Add(new SqlParameter("@min_lon", System.Data.SqlDbType.Real)).Value = MinPoint.Longitude;
                cmd2.Parameters.Add(new SqlParameter("@max_lon", System.Data.SqlDbType.Real)).Value = MaxPoint.Longitude;
                cmd2.CommandType = System.Data.CommandType.Text;
                cmd2.CommandTimeout = int.MaxValue;
                reader = cmd2.ExecuteReader();
                reader.Read();
                int recs = (int)reader[0];
                log.Info("Total records to be processed: " + recs);
                cmd.CommandType = System.Data.CommandType.Text;
                cmd.CommandTimeout = int.MaxValue;
                reader = cmd.ExecuteReader();

                //while (reader.read())
                //{
                //    createconnection(ref reader, ref network);
                //}

                //reader.close();

                int ITER = 0;
                while (reader.Read())
                {
                    ITER++;
                    if ((ITER % 10000) == 0)
                    {
                        log.Info("Building connection " + ITER + " (" + (int)(ITER * 100 / recs) + "%)"); // iter : x = recs : 100
                    }
                    CreateConnection(ref reader, ref Network);
                }
                log.Info("Connections built");
                reader.Close();
            }
            
            DBConnection.CloseConnection();
        }

        private void CreateConnection(ref SqlDataReader reader, ref RoutingNetwork Network)
        {
            TConnection Connection = null;
            string SourceStationID = reader["stop_id"].ToString();
            string DestinationStationID = reader["next_stop_id"].ToString();
            if (SourceStationID != string.Empty & Stops.ContainsKey(SourceStationID))
            {
                if (DestinationStationID != string.Empty & Stops.ContainsKey(DestinationStationID))
                {
                    TNode SourceStation = Stops[SourceStationID];
                    TNode DestinationStation = Stops[DestinationStationID];
                    //
                    int srcArrivalTime = Globals.ConvertTimeToSeconds(reader["arrival_time"].ToString());
                    int srcDepartureTime = Globals.ConvertTimeToSeconds(reader["departure_time"].ToString());
                    int dstArrivalTime = Globals.ConvertTimeToSeconds(reader["next_arrival_time"].ToString());
                    int dstDepartureTime = Globals.ConvertTimeToSeconds(reader["next_departure_time"].ToString());
                    //-----------------------------------------------------------------------------------
                    //Connection = Network.AreConnected(SourceStation, DestinationStation, reader["route_id"].ToString());
                    Connection = null;
                    if (Connection == null)
                        Connection = Network.AddConnection(SourceStation, DestinationStation);
                    //------------------------------------------------------------------------------------
                    Connection.AddTimeTableEntry(
                        new TimeTableEntry(srcArrivalTime, srcDepartureTime, dstArrivalTime, dstDepartureTime,
                            reader["route_id"].ToString(), reader["route_long_name"].ToString(), 
                            reader["route_short_name"].ToString(), reader["route_desc"].ToString(), 
                            reader["route_type"].ToString(), reader["route_url"].ToString(),
                            reader["trip_id"].ToString(), reader["service_id"].ToString(), 
                            GetDates(reader["service_id"].ToString()), reader["agency_id"].ToString())
                        );
                }
            }
        }

        private List<string> GetDates(string serviceId)
        {
            List<string> Dates = new List<string>();
            SqlDataReader reader;

            /* Get dates from calendar table*/
            SqlCommand cmd1 = new SqlCommand();
            cmd1.Connection = DBConnection.Connection;
            cmd1.CommandText = "SELECT * " +
                                "FROM calendar " +
                                "WHERE service_id = @service_id";

            cmd1.Parameters.Add(new SqlParameter("@service_id", System.Data.SqlDbType.NVarChar)).Value = serviceId;

            cmd1.CommandType = System.Data.CommandType.Text;
            reader = cmd1.ExecuteReader();

            while (reader.Read())
            {
                DateTime startDate = DateTime.ParseExact(reader["start_date"].ToString(), "yyyyMMdd", CultureInfo.InvariantCulture);
                DateTime endDate = DateTime.ParseExact(reader["end_date"].ToString(), "yyyyMMdd", CultureInfo.InvariantCulture);
                Boolean monday = (Boolean)reader["monday"];
                Boolean tuesday = (Boolean)reader["tuesday"];
                Boolean wednesday = (Boolean)reader["wednesday"];
                Boolean thursday = (Boolean)reader["thursday"];
                Boolean friday = (Boolean)reader["friday"];
                Boolean saturday = (Boolean)reader["saturday"];
                Boolean sunday = (Boolean)reader["sunday"];

                for (var dt = startDate; dt <= endDate; dt = dt.AddDays(1))
                {
                    if ( (dt.DayOfWeek == DayOfWeek.Monday) && (monday == true) )
                        Dates.Add(dt.ToString("yyyyMMdd"));
                    else if ((dt.DayOfWeek == DayOfWeek.Tuesday) && (tuesday == true))
                        Dates.Add(dt.ToString("yyyyMMdd"));
                    else if ((dt.DayOfWeek == DayOfWeek.Wednesday) && (wednesday == true))
                        Dates.Add(dt.ToString("yyyyMMdd"));
                    else if ((dt.DayOfWeek == DayOfWeek.Thursday) && (thursday == true))
                        Dates.Add(dt.ToString("yyyyMMdd"));
                    else if ((dt.DayOfWeek == DayOfWeek.Friday) && (friday == true))
                        Dates.Add(dt.ToString("yyyyMMdd"));
                    else if ((dt.DayOfWeek == DayOfWeek.Saturday) && (saturday == true))
                        Dates.Add(dt.ToString("yyyyMMdd"));
                    else if ((dt.DayOfWeek == DayOfWeek.Sunday) && (sunday == true))
                        Dates.Add(dt.ToString("yyyyMMdd"));
                }
            }

            reader.Close();
            
            Dates = Dates.Distinct().ToList();


            /* Get dates from calendar_dates table and update the Dates list by adding or removing them */
            SqlCommand cmd2 = new SqlCommand();
            cmd2.Connection = DBConnection.Connection;
            cmd2.CommandText = "SELECT * " +
                                "FROM calendar_dates " +
                                "WHERE service_id = @service_id";

            cmd2.Parameters.Add(new SqlParameter("@service_id", System.Data.SqlDbType.NVarChar)).Value = serviceId;

            cmd2.CommandType = System.Data.CommandType.Text;
            reader = cmd2.ExecuteReader();

            while (reader.Read())
            {
                /* https://developers.google.com/transit/gtfs/reference/calendar_dates-file
                 * A value of 1 indicates that service has been added for the specified date.
                 * A value of 2 indicates that service has been removed for the specified date.
                 */
                if ( String.Compare(reader["exception_type"].ToString(),"1") == 0 )
                    Dates.Add(reader["date"].ToString());
                else 
                    Dates.Remove(reader["date"].ToString());
            }

            reader.Close();

            Dates = Dates.Distinct().ToList();

            return Dates;
        }
    }
}
