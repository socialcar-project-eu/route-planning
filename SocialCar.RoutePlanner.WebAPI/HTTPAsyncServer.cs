using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using log4net;
using log4net.Config;
using System.IO;
using System.Diagnostics;
using SocialCar.RoutePlanner.Carpools;
using SocialCar.RoutePlanner.Traffic;
using SocialCar.RoutePlanner.Routing.Nodes;
using SocialCar.RoutePlanner.Routing;
using System.Configuration;

namespace SocialCar.RoutePlanner.WebAPI
{
    class HTTPAsyncServer
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        public static readonly int maxProcessingTime = 20;
        System.Threading.Timer m_Timer;
        bool exit = false;
        DateTime lastModified;
        string fullPath = "";

        RoutingNetwork productionRoutingNetwork = null;
        //RoutingNetwork processRoutingNetwork = null;
        //RoutingNetwork tmpRoutingNetwork = null;
        bool updateRN = false;

        public static HttpListener listener = new HttpListener();

        static object _ActiveWorkersLock = new object();
        static int _CountOfActiveWorkers;
        private static int DynamicDataIntervalTimeRequest = 60000; //ms

        public HTTPAsyncServer(string url)
        {
            DynamicDataIntervalTimeRequest = int.Parse(ConfigurationManager.AppSettings["DynamicDataIntervalTimeRequest"]);
            m_Timer = new System.Threading.Timer(Timer_Tick_DynamicDataUpdate, null, DynamicDataIntervalTimeRequest, 0);
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            System.IO.FileInfo fileInfo = new System.IO.FileInfo(assembly.Location);
            lastModified = fileInfo.LastWriteTime;
            fullPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            productionRoutingNetwork = Program.routingNetwork1;
            //processRoutingNetwork = Program.routingNetwork2;

            //HttpListener listener = new HttpListener();
            listener.Prefixes.Add(url);
            listener.Start();

            while (true)
            {
                try
                {
                    /* This will be triggered if a network modification occurs */
                    HttpListenerContext context = listener.GetContext();

                    lock (_ActiveWorkersLock)
                        ++_CountOfActiveWorkers;

                    ThreadPool.QueueUserWorkItem(o => HandleRequest(context, productionRoutingNetwork));
                }
                catch (Exception ex)
                {
                    /* Client disconnected or some other error */
                    string errorString = "Error: " + ex;
                    log.Error(errorString);
                    Notification.SendMessageByEmail(Notification.AlarmType.ERROR, errorString);

                }
            }
        }

        void Timer_Tick_DynamicDataUpdate(object state)
        {
            try
            {
                bool CarpoolingEnabled = bool.Parse(ConfigurationManager.AppSettings["CarpoolingEnabled"]);
                bool TrafficEnabled = bool.Parse(ConfigurationManager.AppSettings["TrafficEnabled"]);
                DynamicDataVersion DynamicData = new DynamicDataVersion { };
                CarPoolerDataVersioned CarPoolingTmp = new CarPoolerDataVersioned { };
                TrafficDataVersioned TrafficTmp = new TrafficDataVersioned { };

                /* Get the dynamic data version from the backend web service */
                DynamicData = HTTPRequests.getDynamicDataVersionFromBackend(productionRoutingNetwork.MinPoint, productionRoutingNetwork.MaxPoint);

                if (DynamicData != null)
                {
                    if (CarpoolingEnabled)
                    {
                        /* Carpooling data version */
                        CarPoolingTmp.Version = new CarpoolerVersion(DynamicData.sites.First().carpooling_info.version,
                            DynamicData.sites.First().carpooling_info.updated,
                            DynamicData.sites.First().name,
                            DynamicData.sites.First().carpooling_info.nightly_version,
                            DynamicData.sites.First().carpooling_info.nightly_updated);
                        /* Update the carpooling network */
                        updateCarpoolingNetwork(CarPoolingTmp);
                    }

                    if (TrafficEnabled)
                    {
                        /* Traffic data version */
                        TrafficTmp.Version = new TrafficVersion(DynamicData.sites.First().reports_info.version,
                        DynamicData.sites.First().reports_info.updated,
                        DynamicData.sites.First().name);
                        /* Update the traffic network */
                        updateTrafficNetwork(TrafficTmp);
                    }
                }
            }
            finally
            {
                m_Timer.Change(DynamicDataIntervalTimeRequest, 0);
            }
        }


        void updateCarpoolingNetwork(CarPoolerDataVersioned CarPoolingTmp)
        {
            CarpoolParser CParserTmp = null;

            //JUST FOR TESTING: simulate a new data version
            //CarPoolingTmp.Version.version++;

            if ((Program.CarPooling.Version != null) && (CarPoolingTmp.Version != null) &&
                ((CarPoolingTmp.Version.version != Program.CarPooling.Version.version) ||
                 (CarPoolingTmp.Version.nightly_version != Program.CarPooling.Version.nightly_version)) )
            {
                log.Info("New Carpooling data are available (prevVersion:" + Program.CarPooling.Version.version + ", newVersion:" + CarPoolingTmp.Version.version + ")");
                log.Info("New Carpooling data are available (nightlyPrevVersion:" + Program.CarPooling.Version.nightly_version + ", newNightlyVersion:" + CarPoolingTmp.Version.nightly_version + ")");

                bool updateExternalRides = false;
                if ( (DateTime.Now.Hour >= Program.ExtCarpoolingAvailableUpdateTimeFrom) && (DateTime.Now.Hour < Program.ExtCarpoolingAvailableUpdateTimeTo) )
                    updateExternalRides = true;

                /* Get the carpooling rides list from the backend web service */
                CarPoolingTmp.Carpoolers = HTTPRequests.getCarpoolingDataFromBackend(productionRoutingNetwork.MinPoint, productionRoutingNetwork.MaxPoint, updateExternalRides);

                ////JUST FOR TESTING: modify element
                //CarPoolingTmp.Carpoolers.First().Id = "pizza";

                ////Just for testing: add new element
                //Carpooler test = new Carpooler("drtgwet", 2789, 4);
                //test.WayPoints.Add(new Point(46.013456, 8.943813));
                //test.WayPoints.Add(new Point(46.011596, 8.964293));
                //CarPoolingTmp.Carpoolers.Add(test);

                ////Just for testing: remove element
                //Carpooler testremove = CarPoolingTmp.Carpoolers.First();
                //CarPoolingTmp.Carpoolers.Remove(testremove);

                ////Just for testing: add modified element
                //Carpooler testaddandchange = CarPoolingTmp.Carpoolers.First();
                //testaddandchange.WayPoints.Add(new Point(1, 1));
                //testaddandchange.WayPoints.Add(new Point(2, 2));
                //CarPoolingTmp.Carpoolers.Add(testaddandchange);


                /* Get differences between the old version and the new one */
                CarpoolDiff CarPoolingDiff = CarpoolParser.checkDifferences(Program.CarPooling.Carpoolers, CarPoolingTmp.Carpoolers, updateExternalRides);

                if ((CarPoolingDiff.ElementsToRemove.Count > 0) || (CarPoolingDiff.ElementsToAdd.Count > 0))
                {
                    /* Update the network only when there are no pending threads (Monitor.Wait) */
                    lock (_ActiveWorkersLock)
                    {
                        while (_CountOfActiveWorkers > 0)
                            Monitor.Wait(_ActiveWorkersLock);

                        log.Info("Updating the production network with the carpooling rides");

                        /* Remove the OLD Carpooling connections from the network */
                        if (CarPoolingDiff.ElementsToRemove.Count > 0)
                        {
                            log.Info("Removing " + CarPoolingDiff.ElementsToRemove.Count + " deleted carpooling connections from the processNetwork");

                            foreach (Carpooler el in CarPoolingDiff.ElementsToRemove)
                            {
                                productionRoutingNetwork.RemoveConnection(el);

                                /* Update the original CParser and Carpooling classes */
                                Program.CParser.CarPoolers.Remove(el);
                            }

                            log.Info("ProcessNetwork updated! (" + CarPoolingDiff.ElementsToRemove.Count + " old carpooling connections deleted)");

                            /* Force the network update before processing the next request */
                            updateRN = true;
                        }

                        /* Add the NEW Carpooling connections to the network */
                        if (CarPoolingDiff.ElementsToAdd.Count > 0)
                        {
                            /* Construct the new Carpooling network */
                            log.Info("Adding " + CarPoolingDiff.ElementsToAdd.Count + " new carpooling connections to the processNetwork");
                            CParserTmp = new CarpoolParser(productionRoutingNetwork, CarPoolingDiff.ElementsToAdd);
                            CParserTmp.ConnectWithRoadNetwork();

                            /* Update the original CParser and Carpooling classes */
                            foreach (Carpooler el in CarPoolingDiff.ElementsToAdd)
                            {
                                Program.CParser.CarPoolers.Add(el);
                            }

                            log.Info("Processnetwork updated! (" + CarPoolingDiff.ElementsToAdd.Count + " new carpooling connections added)");

                            /* Force the network update before processing the next request */
                            updateRN = true;
                        }

                        log.Info("Production network updated");
                    }
                }

                if (CarPoolingDiff.ElementsToIgnore.Count > 0)
                {
                    log.Info(CarPoolingDiff.ElementsToIgnore.Count + " rides unchanged");
                }

                /* If there are no modifications too, just update the version number (this happens for example if the driver quickly 
                 * enable and disable a ride, so the version number changes but the content doesn't) 
                 */
                /* Update the carpooling version */
                Program.CarPooling.Version = new CarpoolerVersion(CarPoolingTmp.Version.version, CarPoolingTmp.Version.timestampVersion, CarPoolingTmp.Version.nameSite,
                                                                    CarPoolingTmp.Version.nightly_version, CarPoolingTmp.Version.nightly_timestampVersion);
            }
            else
            {
                if (CarPoolingTmp.Version != null)
                {
                    //log.Info("Carpooling version not changed: version=" + CarPoolingTmp.Version.version + " timestamp=" + CarPoolingTmp.Version.timestampVersion);
                }

            }

        }

        
        void updateTrafficNetwork(TrafficDataVersioned TrafficTmp)
        {
            TrafficParser TParserTmp = null;

            //Just for testing: simulate a new data version
            //TrafficTmp.Version.version++;

            if ((Program.Traffic.Version != null) && (TrafficTmp.Version != null) &&
                        (TrafficTmp.Version.version != Program.Traffic.Version.version))
            {
                log.Info("New Traffic data are available (prevVersion:" + Program.Traffic.Version.version + ", newVersion:" + TrafficTmp.Version.version + ")");
                
                /* Get the traffic reports from the backend web service */
                TrafficTmp.TrafficReport = HTTPRequests.getTrafficDataFromBackend(productionRoutingNetwork.MinPoint, productionRoutingNetwork.MaxPoint, productionRoutingNetwork.TrafficPropagationMaxDistance);

                /* Get differences between the old version and the new one */
                TrafficDiff TrafficDiff = TrafficParser.checkDifferences(Program.Traffic.TrafficReport, TrafficTmp.TrafficReport);

                if ((TrafficDiff.ElementsToRemove.Count > 0) || (TrafficDiff.ElementsToAdd.Count > 0))
                {
                    /* Update the network only when there are no pending threads (Monitor.Wait) */
                    lock (_ActiveWorkersLock)
                    {
                        while (_CountOfActiveWorkers > 0)
                            Monitor.Wait(_ActiveWorkersLock);

                        log.Info("Updating the production network with the traffic reports");

                        /* Remove the OLD Traffic reports from the network */
                        if (TrafficDiff.ElementsToRemove.Count > 0)
                        {
                            log.Info("Removing " + TrafficDiff.ElementsToRemove.Count + " deleted traffic reports from the processNetwork");
                            int i = 1;
                            foreach (TrafficReport el in TrafficDiff.ElementsToRemove)
                            {
                                productionRoutingNetwork.RemoveTrafficReport(el);

                                /* Update the original TrafficParser */
                                Program.TParser.TrafficReport.Remove(el);
                                i++;
                            }

                            log.Info("ProcessNetwork updated! (" + TrafficDiff.ElementsToRemove.Count + " old traffic reports deleted)");
                            
                            /* Force the network update before processing the next request */
                            updateRN = true;
                        }

                        /* Add the NEW Traffic reports to the network */
                        if (TrafficDiff.ElementsToAdd.Count > 0)
                        {
                            /* Add to each connection the traffic report */
                            log.Info("Adding the new " + TrafficDiff.ElementsToAdd.Count + " traffic connections to the processNetwork");
                            TParserTmp = new TrafficParser(productionRoutingNetwork, TrafficDiff.ElementsToAdd);
                            TParserTmp.UpdateNetworkWithTrafficReport();

                            /* Update the original TParser and Traffic report classes */
                            foreach (TrafficReport el in TrafficDiff.ElementsToAdd)
                            {
                                Program.TParser.TrafficReport.Add(el);
                            }

                            log.Info("Processnetwork updated! (" + TrafficDiff.ElementsToAdd.Count + " new traffic reports added)");

                            /* Force the network update before processing the next request */
                            updateRN = true;
                        }

                        log.Info("Production network updated with the traffic reports");
                    }
                }


                /* If there are no modifications too, just update the version number (this happens for example if the driver quickly 
                 * enable and disable a ride, so the version number changes but the content doesn't) 
                 */
                /* Update the traffic report version */
                Program.Traffic.Version = new TrafficVersion(TrafficTmp.Version.version, TrafficTmp.Version.timestampVersion, TrafficTmp.Version.nameSite);
            }

            else
            {
                if (TrafficTmp.Version != null)
                {
                    //log.Info("Traffic version not changed: version=" + TrafficTmp.Version.version + " timestamp=" + TrafficTmp.Version.timestampVersion);
                }
            }
        }
        
        private void HandleRequest(object state, RoutingNetwork productionRoutingNetwork)
        {
            try
            {
                Message OutMsg = new Message { };
                var context = (HttpListenerContext)state;
                double queryDuration = 0;

                context.Response.SendChunked = true;
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                string HttpMethod = context.Request.HttpMethod;
                string RequestType = context.Request.Url.ToString().Split('/').Last().Split('?').First();
                string QueryString = context.Request.Url.ToString().Split('/').Last().Remove(0, RequestType.Length + 1);

                log.Info("idRequest:" + context.Request.RequestTraceIdentifier + " - ipClient:" + context.Request.RemoteEndPoint.Address + " - userAgent:" + context.Request.UserAgent);
                log.Info("idRequest:" + context.Request.RequestTraceIdentifier + " - httpMethod:" + HttpMethod + " - reqType:" + RequestType + " - query:" + QueryString);

                switch (HttpMethod)
                {
                    case "GET":
                        Dictionary<string, string> Params = ProcessQueryString(QueryString);
                        string JsonStringResponse = string.Empty;
                        byte[] Bytes = null;

                        DateTime a = DateTime.Now;

                        switch (RequestType)
                        {
                            case "route":
                                OutMsg = RequestHandler.HandleRouteRequest(Params, context, productionRoutingNetwork);
                                break;
                            default:
                                /* Wrong service */
                                OutMsg.Result = false;
                                OutMsg.Error.Code = OutMsg.Error.GetDefaultValueAttributeFromEnumValue(ErrorCodes.WRONG_REQUEST_TYPE);
                                OutMsg.Error.Message = OutMsg.Error.GetDescriptionFromEnumValue(ErrorCodes.WRONG_REQUEST_TYPE);
                                OutMsg.Error.Params.Add(nameof(HttpMethod), HttpMethod);
                                OutMsg.Error.Params.Add(nameof(RequestType), RequestType);
                                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                                break;
                        }

                        queryDuration = DateTime.Now.Subtract(a).TotalSeconds;

                        /* Add info to the message */
                        int unixDateTime = Globals.GetUnixTimeStampOutMsg(DateTime.Now);
                        OutMsg.Error.TimestampRequest = unixDateTime.ToString();
                        OutMsg.Error.HttpRequest = QueryString;
                        OutMsg.Error.HttpMethod = HttpMethod;
                        OutMsg.Error.ResponseCode = context.Response.StatusCode.ToString();
                        OutMsg.Error.ResponseDescription = context.Response.StatusDescription;
                        OutMsg.Error.IdRequest = context.Request.RequestTraceIdentifier.ToString();
                        OutMsg.Error.RequestType = RequestType;
                        OutMsg.Error.IpClient = context.Request.RemoteEndPoint.Address.ToString();
                        OutMsg.Error.UserAgent = context.Request.UserAgent.ToString();
                        OutMsg.Error.Duration = queryDuration.ToString();

                        JsonStringResponse = JsonConvert.SerializeObject(OutMsg);
                        Bytes = Encoding.UTF8.GetBytes(JsonStringResponse);
                        context.Response.OutputStream.Write(Bytes, 0, Bytes.Length);

                        /* Logging */
                        if (OutMsg.Result == false)
                            log.Error("idRequest:" + context.Request.RequestTraceIdentifier + " - json: " + JsonStringResponse);
                        else
                            if (OutMsg.Trips.Count == 0)
                                log.Warn("idRequest:" + context.Request.RequestTraceIdentifier + " - json: " + JsonStringResponse);
                            else
                                log.Info("idRequest:" + context.Request.RequestTraceIdentifier + " - json: " + JsonStringResponse);

                        break;
                }

                context.Response.Close();

                /* Logs and mail notification */
                if (context.Response.StatusCode == 200)
                    log.Info("idRequest:" + context.Request.RequestTraceIdentifier + " - statusCode:" + context.Response.StatusCode + " - statusDescr:" + context.Response.StatusDescription + " - duration(s):" + queryDuration);
                else
                {
                    string errorString = "idRequest:" + context.Request.RequestTraceIdentifier + " - statusCode:" + context.Response.StatusCode + " - statusDescr:" + context.Response.StatusDescription + " - duration(s):" + queryDuration;
                    log.Error(errorString);
                    string statusCode = context.Response.StatusCode.ToString();
                    Notification.AlarmType alarm = 0;
                    if (statusCode[0] == '4')
                        alarm = Notification.AlarmType.WARNING;
                    else
                        alarm = Notification.AlarmType.ERROR;

                    Notification.SendMessageByEmail(alarm, JsonConvert.SerializeObject(OutMsg, Formatting.Indented), context.Response.StatusCode);
                }

                /* Send a further notification if the RP takes too much time */
                if (queryDuration > maxProcessingTime)
                {
                    Notification.AlarmType alarm = Notification.AlarmType.TIME;
                    OutMsg.Error.Message = OutMsg.Error.GetDescriptionFromEnumValue(ErrorCodes.RP_PROCESSING_TIME_TOO_LONG);
                    OutMsg.Error.Code = OutMsg.Error.GetDefaultValueAttributeFromEnumValue(ErrorCodes.RP_PROCESSING_TIME_TOO_LONG);
                    Notification.SendMessageByEmail(alarm, JsonConvert.SerializeObject(OutMsg, Formatting.Indented), context.Response.StatusCode);
                }

                /* Send a further notification id there are some info messages from RP */
                if (OutMsg.Error.RPMessage.RoutePlannerCodes.Count() > 0)
                {
                    Notification.AlarmType alarm = Notification.AlarmType.INFO;
                    foreach (var code in OutMsg.Error.RPMessage.RoutePlannerCodes)
                    {
                        OutMsg.Error.Message += "; " + OutMsg.Error.RPMessage.GetDescriptionFromEnumValue(code);
                        OutMsg.Error.Code += "; " + OutMsg.Error.RPMessage.GetDefaultValueAttributeFromEnumValue(code);
                    }
                    Notification.SendMessageByEmail(alarm, JsonConvert.SerializeObject(OutMsg, Formatting.Indented), context.Response.StatusCode);
                }


            }
            catch (Exception ex)
            {
                /* Client disconnected or some other error */
                string errorString = "Client disconnected or some other error: " + ex;
                log.Error(errorString);
                Notification.SendMessageByEmail(Notification.AlarmType.ERROR, errorString);
            }
            finally
            {
                lock (_ActiveWorkersLock)
                {
                    --_CountOfActiveWorkers;
                    Monitor.PulseAll(_ActiveWorkersLock);
                }
            }
        }

        private static Dictionary<string, string> ProcessQueryString(string QueryString)
        {
            string[] Params = QueryString.Split('&');
            Dictionary<string, string> Dic = new Dictionary<string, string>();

            foreach (string Param in Params)
            {
                Dic.Add(Param.Split('=').First(), Param.Split('=').Last());
            }

            return Dic;
        }

    }
}
