using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Management.Common;
using System.Data.SqlClient;
using System.Data.OleDb;
using System.IO;
using System.Data;
using System.Globalization;
using System.Configuration;
using log4net;
using log4net.Config;
using System.Web;
using System.Xml.Linq;
using IniParser;
using IniParser.Model;
using CsvHelper;
using System.Collections;
using System.Text.RegularExpressions;

namespace SocialCar.RoutePlanner.GTFS_DB
{
    class Program
    {

        /* 
         * This array contains the private and foreign keys which should be changed in order to make their values unique
         * A prefix with the name of the feed (followed by the chars "###") will be put in front of the original value (ex. "A001" => "feed1###A001")
         */
        public static readonly string[] keys = { "route_id", "agency_id", "stop_id", "trip_id", "service_id"};

        /* GTFS file extension allowed */
        public static readonly string[] csvExt = { ".txt", ".csv" };

        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /* File list used to generate the DB */
        private static readonly string[] list_files = { "agency.txt", "calendar.txt", "calendar_dates.txt", "stops.txt", "routes.txt", "trips.txt", "stop_times.txt" };



        static void Main(string[] args)
        {
            string serverName = "";
            string DBInstanceName = ConfigurationManager.AppSettings["DBInstanceName"];
            if (DBInstanceName != null)
                serverName = Environment.MachineName + "\\" + DBInstanceName;
            else
                serverName = Environment.MachineName + "\\SQLEXPRESS";

            string database = ConfigurationManager.AppSettings["DBName"];
            string ConnString = String.Format("Data Source={0};Initial Catalog={1};Integrated Security=SSPI", serverName, database);
            string GTFSFolder = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory + ConfigurationManager.AppSettings["dataPath"] + ConfigurationManager.AppSettings["GTFSFolder"]);
            string GTFS_schemaIniFileName = ConfigurationManager.AppSettings["GTFS_schemaIniFileName"];
            string GTFS_DB_Mapping = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory + ConfigurationManager.AppSettings["dbScriptPath"] + ConfigurationManager.AppSettings["GTFS_DB_Mapping"]);
            string scriptFile = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory + ConfigurationManager.AppSettings["dbScriptPath"] + ConfigurationManager.AppSettings["DBScriptFile"]);

            log.Info("DB server: " + serverName);
            log.Info("DB name: " + database);
            log.Info("ConnString: " + ConnString);
            log.Info("GTFSFolder: " + GTFSFolder);
            log.Info("GTFS_schemaIniFileName: " + GTFS_schemaIniFileName);
            log.Info("GTFS_DB_Mapping: " + GTFS_DB_Mapping);
            log.Info("scriptFile: " + scriptFile);

            //get feeds
            string[] gtfsList = GetFeeds(GTFSFolder);
    

            /*** PRE-PROCESSING ***/
            /* Generate the schema.ini configurator file */
            GenerateIniFile(gtfsList, GTFSFolder, GTFS_DB_Mapping, GTFS_schemaIniFileName);

            /* Validate feeds */
            List<string> invalidGtfs = new List<string>();
            invalidGtfs = ValidateFeeds(gtfsList, GTFS_schemaIniFileName);

            /* Check the agency_id field */
            if (invalidGtfs.Count() == 0)
                invalidGtfs = CheckAgencyID(gtfsList, GTFSFolder);

            ///* Check the agency_id field */
            //if (invalidGtfs.Count() == 0)
            //    invalidGtfs = CheckTimeZones(gtfsList, GTFSFolder););

            /*** PROCESSING ***/
            /* Insert feeds into db */
            FillDB(gtfsList, serverName, database, ConnString, invalidGtfs, scriptFile);


        }


        /**
  * 
  */
        //static private List<string> CheckTimeZones(string[] gtfsList, string GTFSFolder)
        //{
        //    List<string> invalidGtfs = new List<string>();
        //    Dictionary<string, int> dict = new Dictionary<string, int>();

        //    foreach (string feed in gtfsList)
        //    {
        //        FileInfo[] Files = new DirectoryInfo(feed).GetFiles();

        //        bool foundAgency = false;
        //        bool foundRoutes = false;

        //        foreach (FileInfo f in Files)
        //        {
        //            // Consider only the comma separated value files (see extension list csvExt)
        //            int index = Array.IndexOf(csvExt, f.Extension);
        //            if (index != -1)
        //            {
        //                // Check if the attribute agency_id exists and how many 
        //                var file = new StreamReader(f.FullName);
        //                var csv = new CsvReader(file);
        //                csv.ReadHeader();

        //                log.Debug(csv.FieldHeaders);

        //                if (String.Compare(f.Name, "agency.txt") == 0)
        //                {
        //                    foreach (string csvAttribute in csv.FieldHeaders)
        //                    {
        //                        if (String.Compare(csvAttribute, "agency_id") == 0)
        //                        {
        //                            foundAgency = true;
        //                            break;
        //                        }

        //                    }
        //                }

        //                if (String.Compare(f.Name, "routes.txt") == 0)
        //                {
        //                    foreach (string csvAttribute in csv.FieldHeaders)
        //                    {
        //                        if (String.Compare(csvAttribute, "agency_id") == 0)
        //                        {
        //                            foundRoutes = true;
        //                            break;
        //                        }

        //                    }
        //                }

        //                file.Close();
        //            }
        //        }

        //        // Should match
        //        if (foundAgency != foundRoutes)
        //        {
        //            string error = "feed:" + feed + " agency.txt/agency_id:" + foundAgency + " routes.txt/agency_id:" + foundRoutes;
        //            log.Error(error);
        //            invalidGtfs.Add(error);
        //        }

        //    }



        //    return invalidGtfs;
        //}


        /**
         * 
         */
        static private List<string> CheckAgencyID(string[] gtfsList, string GTFSFolder)
        {
            List<string> invalidGtfs = new List<string>();
            Dictionary <string, int> dict = new Dictionary<string, int>();

            foreach (string feed in gtfsList)
            {
                FileInfo[] Files = new DirectoryInfo(feed).GetFiles();

                bool foundAgency = false;
                bool foundRoutes = false;

                foreach (FileInfo f in Files)
                {
                    // Consider only the comma separated value files (see extension list csvExt)
                    int index = Array.IndexOf(csvExt, f.Extension);
                    if (index != -1)
                    {
                        // Check if the attribute agency_id exists and how many 
                        var file = new StreamReader(f.FullName);
                        var csv = new CsvReader(file);
                        csv.ReadHeader();

                        log.Debug(csv.FieldHeaders);

                        if (String.Compare(f.Name,"agency.txt")==0)
                        {
                            foreach (string csvAttribute in csv.FieldHeaders)
                            {
                                if (String.Compare(csvAttribute, "agency_id") == 0)
                                {
                                    foundAgency = true;
                                    break;
                                }

                            }
                        }

                        if (String.Compare(f.Name, "routes.txt") == 0)
                        {
                            foreach (string csvAttribute in csv.FieldHeaders)
                            {
                                if (String.Compare(csvAttribute, "agency_id") == 0)
                                {
                                    foundRoutes = true;
                                    break;
                                }

                            }
                        }

                        file.Close();
                    }
                }
                
                // Should match
                if (foundAgency != foundRoutes)
                {
                    string error = "feed:" + feed + " agency.txt/agency_id:" + foundAgency + " routes.txt/agency_id:" + foundRoutes;
                    log.Error(error);
                    invalidGtfs.Add(error);
                }

            }



            return invalidGtfs;
        }



        /**
         * Generate the schema.ini file 
         * @param gtfsList: feed list into the gtfs source path
         * @param GTFSFolder: gtfs source path
         * @param GTFS_DB_Mapping: file mapping db attributes-datatype
         * @param GTFS_schemaIniFileName: "schema.ini" file name
         */
        static private void GenerateIniFile(string[] gtfsList, string GTFSFolder, string GTFS_DB_Mapping, string GTFS_schemaIniFileName)
        {
            var parser = new FileIniDataParser();

            IniData dataMap = parser.ReadFile(GTFS_DB_Mapping);


            foreach (string feed in gtfsList)
            {
                string iniFile = feed + "\\" + GTFS_schemaIniFileName;
                IniData dataOut = new IniData();

                foreach (SectionData dMapSection in dataMap.Sections)
                {
                    string sectionName = dMapSection.SectionName;
                    string fileName = feed + "\\" + sectionName;
                    log.Info("Validate file: " + fileName);
                    if (!File.Exists(fileName))
                    {
                        log.Warn("File " + fileName + " not found");
                    }
                    else
                    {
                        var file = new StreamReader(fileName);
                        var csv = new CsvReader(file);
                        csv.ReadHeader();
                        log.Debug(csv.FieldHeaders);

                        //Write attributes into the ini parserOut
                        int i = 1;
                        foreach (string csvAttribute in csv.FieldHeaders)
                        {
                            bool found = false;

                            foreach (KeyData attributeDmapSection in dMapSection.Keys)
                            {
                                if ((String.Compare(csvAttribute, attributeDmapSection.KeyName) == 0))
                                {
                                    string value = csvAttribute + " " + attributeDmapSection.Value;
                                    string key = "Col" + i;
                                    dataOut[sectionName].AddKey(key, value);
                                    //log.Info("Csv attribute " + csvAttribute + " found");
                                    found = true;
                                    i++;
                                    break;
                                }
                            }

                            if (!found)
                            {
                                log.Error("Csv attribute " + csvAttribute + " not found into the map ini file, section:" + sectionName);
                            }

                            file.Close();
                        }

                    }
                }

                parser.WriteFile(iniFile, dataOut);
            }
        }



        /**
         * Validate feeds is used to check the schema.ini file (which MUST be present in the feed folder)
         * All attributes will be checked and compared with the csv solumns
         * @param gtfsList: feed list into the gtfs source path
         * @param GTFSFolder: gtfs source path
         * @return invalidGtfs: invalid feed and file list
         */
        static private List<string> ValidateFeeds(string[] gtfsList, string GTFS_schemaIniFileName)
        {
            List<string> invalidGtfs = new List<string>();
            int j = 0;

            foreach (string feed in gtfsList)
            {
                string cfgFilePath = feed + "\\" + GTFS_schemaIniFileName;
                string feedName = Path.GetFileName(feed);

                var parser = new FileIniDataParser();
                IniData data = parser.ReadFile(cfgFilePath);

                foreach (SectionData d in data.Sections)
                {
                    string fileName = feed + "\\" + d.SectionName;
                    log.Info("Validate file: " + fileName);
                    if (!File.Exists(fileName))
                    {
                        log.Warn("File not found");
                    }
                    else
                    {
                        var file = new StreamReader(fileName);
                        var csv = new CsvReader(file);
                        csv.ReadHeader();
                        log.Info(csv.FieldHeaders);

                        //Check attributes, should match
                        int i = 0;
                        foreach (KeyData e in d.Keys)
                        {
                            string[] values = e.Value.Split(null);
                            //log.Info("Element:" + e.KeyName + " Attribute:" + values[0] + " DataType:" + values[1] + " csvName:" + csv.FieldHeaders[i]);
                            if ((i < csv.FieldHeaders.Length) && (String.Compare(values[0], csv.FieldHeaders[i]) != 0))
                            {
                                string error = fileName + " (Element:" + e.KeyName + " iniAttribute:" + values[0] + " DataType:" + values[1] + " csvAttribute:" + csv.FieldHeaders[i] + ")";
                                log.Error("Mismatch: " + error);
                                invalidGtfs.Add(error);
                            }

                            i++;
                        }

                        file.Close();
                    }
                }
            }

            return invalidGtfs;
        }



        /**
         * Get all feeds from the source path
         * @param GTFSFolder: gtfs source path
         * @return gtfsList: feed list
         */
        static private string[] GetFeeds(string GTFSFolder)
        {
            string[] gtfsList = Directory.GetDirectories(GTFSFolder);
            log.Info("Detected " + gtfsList.Count() + " GTFS feed");

            for (int i=0; i < gtfsList.Count(); i++)
            {
                string gtfsName = Path.GetFileName(gtfsList[i]);
                log.Info("Feed " + (i+1) + ": " + gtfsList[i]);
            }

            return gtfsList;
        }



        /**
         * Generate DataBase
         */ 
        static private void FillDB(string[] gtfsList, string server, string database, string ConnString, List<string> invalidGtfs, string scriptFile)
        {
            try
            {
                if (invalidGtfs.Count() == 0)
                {
                    // Create DB using the sql script
                    log.Info("Creating the data base.");
                    CreateDB(server, scriptFile);
                    log.Info("Data base created.");


                    foreach (string gtfs in gtfsList)
                    {
                        FileInfo[] Files = new DirectoryInfo(gtfs).GetFiles();

                        string gtfsName = Path.GetFileName(gtfs);

                        log.Info("Processing feed: " + gtfsName);

                        foreach (string item in list_files)
                        {
                            if (File.Exists(gtfs + "\\" + item))
                            {
                                log.Info("Processing file: " + item);
                                ProcessFile(gtfsName, ConnString, new FileInfo(gtfs + "\\" + item));
                                log.Info("Finished processing file: " + item);
                            }
                            else
                            {
                                log.Warn("File not found: " + item);
                            }
                        }
                        log.Info("Finished processing feed: " + gtfsName);
                    }
                }
                else
                {
                    for (int i = 0; i < invalidGtfs.Count(); i++)
                    {
                        string gtfsName = Path.GetFileName(invalidGtfs[i]);
                        log.Error("Invalid Feed " + (i + 1) + ": " + invalidGtfs[i]);
                    }

                }
            }
            catch (Exception ex)
            {
                log.Error(ex.Message);
                log.Error(ex.InnerException.Message);
                log.Error(ex.StackTrace);
                Environment.Exit(-1);
            }

            log.Info("Successful!");
            Environment.Exit(0);
        }



        /**
         * 
         */ 
        static void CreateDB(string server, string path)
        {
            string DBName = ConfigurationManager.AppSettings["DBName"];
            DBName = "master";
            string ConnString = String.Format("Data Source={0};Initial Catalog={1};Integrated Security=SSPI;Trusted_Connection=True", server, DBName);

            try
            {
                FileInfo fileInfo = new FileInfo(path);
                string script = fileInfo.OpenText().ReadToEnd();
                using (SqlConnection connection = new SqlConnection(ConnString))
                {
                    Server srvr = new Server(new ServerConnection(connection));
                    srvr.ConnectionContext.ExecuteNonQuery(script);
                }
            }
            catch (Exception ex)
            {
                log.Error(ex.Message);
                log.Error(ex.InnerException.Message);
                log.Error(ex.StackTrace);
                Environment.Exit(-1);
            }
        }



        /**
         * 
         */ 
        static private void ProcessFile(string GTFSname, string ConnString, FileInfo FILE)
        {
            try
            {
                DataTable dt = new DataTable();

                string pathOnly = Path.GetDirectoryName(FILE.FullName);
                string fileName = Path.GetFileName(FILE.Name);

                //Microsoft.Jet.OLEDB.4.0
                //Microsoft.ACE.OLEDB.12.0
                string csConnectionString = String.Format
                    (
                        @"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={0};" +
                        "Extended Properties=\"Text;ImportMixedTypes=Text;HDR=YES;FMT=Delimited;IMEX=1;Readonly=False\"",
                        pathOnly
                    );

                string sql = @"SELECT * FROM [" + fileName + "]";

                log.Info("Reading file: " + fileName);

                int startIndx = 0;
                int Cnt = 0;
                int BatchSize = 500000;

                while (true)
                {
                    OleDbConnection connection = new OleDbConnection(csConnectionString);
                    OleDbCommand command = new OleDbCommand(sql, connection);
                    OleDbDataAdapter adapter = new OleDbDataAdapter(command);

                    command.CommandTimeout = int.MaxValue;
                    dt.Locale = CultureInfo.CurrentCulture;

                    adapter.Fill(startIndx, BatchSize, dt);

                    if (dt.Rows.Count == 0) break;

                    using (SqlBulkCopy bulkCopy = new SqlBulkCopy(ConnString))
                    {
                        bulkCopy.BulkCopyTimeout = int.MaxValue;
                        bulkCopy.BatchSize = BatchSize;

                        if ( (String.Compare(FILE.Name,"agency.txt") == 0) || (String.Compare(FILE.Name, "route.txt") == 0))
                            if (!dt.Columns.Contains("agency_id"))
                                dt.Columns.Add("agency_id");

                        foreach (DataColumn c in dt.Columns)
                        {
                            ////DEBUG: print all records
                            //if ((fileName == "trips.txt") && (string.Compare(c.ToString(), "route_short_name") == 0))
                            //{
                            //    int n = 0;
                            //    foreach (DataRow row in dt.Rows)
                            //    {
                            //        //log.Info(fileName + " - " + n + " - " + c + " - " + row[c]);
                            //        if (string.Compare(row[c].ToString(), "") == 0)
                            //        {
                            //            bool p = true;
                            //            log.Info(fileName + " - " + n + " - " + c + " - " + row[c]);
                            //        }
                            //        //row[c] = 1;
                            //        n++;
                            //    }
                            //}


                            /* Fix only for the Ticino GTFS feed stop_times files stop_sequence missing values */
                            //if ((fileName == "stop_times.txt") && (string.Compare(c.ToString(), "stop_sequence") == 0))
                            //{
                            //    int n = 0;
                            //    for (int i = 0; i < dt.Rows.Count; i++)
                            //    {
                            //        //DEBUG //log.Info(fileName + " - " + n + " - " + c + " - " + dt.Rows[i][c]);
                            //        if (string.Compare(dt.Rows[i][c].ToString(), "") == 0)
                            //        {
                            //            bool p = true;
                            //            if (i < dt.Rows.Count - 1)
                            //            {
                            //                if (int.Parse(dt.Rows[i + 1][c].ToString()) < int.Parse(dt.Rows[i - 1][c].ToString()))
                            //                {
                            //                    if (int.Parse(dt.Rows[i + 1][c].ToString()) == 1)
                            //                    {
                            //                        dt.Rows[i][c] = (int.Parse(dt.Rows[i - 1][c].ToString()) + 1).ToString();
                            //                    }
                            //                    else if (int.Parse(dt.Rows[i + 1][c].ToString()) == 2)
                            //                    {
                            //                        dt.Rows[i][c] = (int.Parse(dt.Rows[i + 1][c].ToString()) - 1).ToString();
                            //                    }
                            //                }
                            //                else
                            //                {
                            //                    dt.Rows[i][c] = (int.Parse(dt.Rows[i - 1][c].ToString()) + 1).ToString();
                            //                }
                            //            }
                            //        }
                            //        n++;
                            //    }
                            //}

                            //add gtfs name to the primary and foreign key values
                            int index = Array.IndexOf(keys, c.ColumnName);
                            if (index != -1)
                            {
                                foreach (DataRow row in dt.Rows)
                                {
                                    row[c] = GTFSname + "###" + row[c];
                                    //log.Info("INFO: " + row[c] + " added.");
                                }
                            }

                            bulkCopy.ColumnMappings.Add(c.ColumnName, c.ColumnName);
                        }

                        bulkCopy.DestinationTableName = FILE.Name.Substring(0, FILE.Name.IndexOf('.'));

                        bulkCopy.WriteToServer(dt);
                        bulkCopy.Close();
                    }

                    Cnt += dt.Rows.Count;
                    startIndx += BatchSize;

                    dt.Rows.Clear();

                    adapter.Dispose();
                    command.Dispose();

                    connection.Close();
                    connection.Dispose();

                    log.Info(Cnt + " records added.");
                }

                log.Info(Cnt + " records added.");
            }
            catch (Exception ex)
            {
                log.Error(ex.Message);
                log.Error(ex.InnerException.Message);
                log.Error(ex.StackTrace);
                Environment.Exit(-1);
            }
        }
    }
}
