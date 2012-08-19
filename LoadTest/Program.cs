using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Xml;
using System.IO;
using System.Threading;
using System.Collections.Specialized;
using System.Web;
using System.Net;
using System.Text.RegularExpressions;

namespace LoadTest {
    class Program {
        // possible args: duration, threads, target url, postdata, etc.
        static void Main(string[] args) {
            Console.WriteLine("C# Load Testing App");
            Console.Write("Enter the test xml file name: ");
            string xmlFile = Console.ReadLine();

            // Attempt to open the xml file specified by the user
            XmlDocument test = new XmlDocument();
            try {
                test.Load(string.Format("../../../{0}", xmlFile));
            }
            catch (FileNotFoundException e) {
                Console.WriteLine(e.Message);
                Console.ReadKey();
                Environment.Exit(2);
            }

            // Extract the configuration and test details
            int maxThreads = Convert.ToInt16(test.SelectSingleNode("//configuration/threads").InnerText);
            int rampUp = Convert.ToInt16(test.SelectSingleNode("//configuration/rampUp").InnerText);

            int duration = int.MinValue;
            bool isTimedRun = int.TryParse(test.SelectSingleNode("//configuration/duration").InnerText, out duration);
            
            XmlNodeList requestsData = test.SelectNodes("//request");

            Console.WriteLine(requestsData.Count);

            List<RequestInfo> rInfo = new List<RequestInfo>();
            NameValueCollection pData = new NameValueCollection();
            for (int i = 0; i < requestsData.Count; i++) {
                if (requestsData.Item(i).SelectSingleNode("type").InnerText.ToLower() == "post") {
                    XmlNodeList pDataXml = requestsData.Item(i).SelectNodes("//dataItem");
                    for (int j = 0; j < pDataXml.Count; j++) {
                        pData.Add(pDataXml.Item(j).SelectSingleNode("name").InnerText, pDataXml.Item(j).SelectSingleNode("value").InnerText);
                    }
                }

                rInfo.Add(new RequestInfo() { url = requestsData.Item(i).SelectSingleNode("url").InnerText, requestType = requestsData.Item(i).SelectSingleNode("type").InnerText, postData = pData });
            }

            // Configure the test engine
            ThreadPool.SetMaxThreads(maxThreads, maxThreads);
            ThreadPool.SetMinThreads(maxThreads, maxThreads);

            Actions action = new Actions();
            DateTime stopTime = DateTime.Now.AddSeconds(duration);

            if (isTimedRun) {
                while (DateTime.Now < stopTime) {
                    int workerThreads;
                    int portThreads;

                    ThreadPool.GetAvailableThreads(out workerThreads, out portThreads);

                    if (workerThreads > 0) {
                        ThreadPool.QueueUserWorkItem(new WaitCallback(action.MakeRequests), new TestInfo() { testData = rInfo });
                        Thread.Sleep(250);
                    }
                    else {
                        var timeLeft = (stopTime - DateTime.Now);
                        Console.WriteLine("Queue is full... time left {0:0}:{1:00}", timeLeft.Minutes, timeLeft.Seconds);
                        Thread.Sleep(1000);
                    }
                }
            }
            else {
                for (int i = 0; i < maxThreads; i++) {
                    ThreadPool.QueueUserWorkItem(new WaitCallback(action.MakeRequests), new TestInfo() { testData = rInfo });
                    Thread.Sleep(250);
                }
            }

            // Allow the existing requests to burnoff before exiting the application
            int threadsLeft = 1;
            int portThreadsLeft = 1;
            DateTime burnoff = DateTime.Now.AddSeconds(180);

            Console.WriteLine("Finished making requests, entering the burnoff period.");
            while (DateTime.Now < burnoff && threadsLeft < maxThreads) {
                ThreadPool.GetAvailableThreads(out threadsLeft, out portThreadsLeft);

                Console.WriteLine("Waiting for {0} thread(s) to finish...", maxThreads - threadsLeft);

                Thread.Sleep(1000);
            }

            Console.WriteLine("Test run completed. Press any key to close the application.");
            Console.ReadKey();
        }
    }

    public class TestInfo {
        public List<RequestInfo> testData { get; set; }
    }

    public class RequestInfo {
        public string url { get; set; }
        public string requestType { get; set; }
        public NameValueCollection postData { get; set; }
    }

    public class Actions {
        private int totalTransactions = 0;

        public void MakeRequests(Object stateInfo) {
            var requests = stateInfo as TestInfo;

            totalTransactions++;
            int currentTransaction = totalTransactions;
            Console.WriteLine("Begin Transaction # {0}", currentTransaction);

            CookieCollection persistCookies = new CookieCollection();

            // Start the stopwatch
            var timer = new System.Diagnostics.Stopwatch();
            timer.Start();

            foreach (var requestData in requests.testData) {
                Uri firstUri = new Uri(requestData.url);

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(requestData.url);

                if (requestData.requestType.ToLower() == "post") {
                    HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                    
                    StringBuilder sb = new StringBuilder();
                    using (StreamReader stream = new StreamReader(response.GetResponseStream())) {
                        string strLine;
                        while ((strLine = stream.ReadLine()) != null) {
                            if (strLine.Length > 0)
                                sb.Append(strLine);
                        }
                        stream.Close();
                    }
                    response.Close();

                    string rawData = sb.ToString();

                    // Find the inputs on the page
                    Regex filters = new Regex(@"<input.+?\/?>");
                    MatchCollection matches = filters.Matches(rawData);
                    XmlDocument xmlDoc = new XmlDocument();
                    NameValueCollection formData = new NameValueCollection();

                    // Extract the name/value pairs of the input names and values
                    for (int i = 0; i < matches.Count; i++) {
                        xmlDoc.LoadXml(matches[i].Value);

                        XmlNode node = xmlDoc.DocumentElement.Attributes["value"];

                        string value = node != null ? node.Value : string.Empty;
                        formData.Add(xmlDoc.DocumentElement.Attributes["name"].Value, value);
                    }

                    foreach (string key in requestData.postData.Keys) {
                        if (formData.AllKeys.Contains(key)) {
                            formData[key] = requestData.postData[key];
                        }
                    }

                    // Post the request
                    HttpWebRequest requestPost = (HttpWebRequest)WebRequest.Create(requestData.url);
                    byte[] byteArrayPostAttendance = Encoding.UTF8.GetBytes(Helpers.GenerateTargetRequestData(formData));
                    requestPost.ContentType = "application/x-www-form-urlencoded";
                    requestPost.Method = "POST";
                    requestPost.AllowAutoRedirect = false;
                    requestPost.CookieContainer = new CookieContainer();
                    requestPost.ContentLength = byteArrayPostAttendance.Length;

                    using (Stream postDataStreamNew = requestPost.GetRequestStream()) {
                        postDataStreamNew.Write(byteArrayPostAttendance, 0, byteArrayPostAttendance.Length);
                    }

                    HttpWebResponse requestPostResponse = (HttpWebResponse)requestPost.GetResponse();
                    persistCookies = requestPostResponse.Cookies;

                    if (requestPostResponse.StatusCode == HttpStatusCode.Found) {
                        requestPostResponse.Close();

                        string target = string.Empty;

                        if (Uri.IsWellFormedUriString(requestPostResponse.Headers["Location"], UriKind.Absolute)) {
                            target = requestPostResponse.Headers["Location"];
                        }
                        else {
                            target = firstUri.Scheme + "://" + firstUri.Authority + requestPostResponse.Headers["Location"];
                        }

                        HttpWebRequest redirectRequest = (HttpWebRequest)WebRequest.Create(target);
                        CookieContainer cookieContainer = new CookieContainer();
                        foreach (Cookie cookie in persistCookies) {
                            cookieContainer.Add(cookie);
                        }
                        redirectRequest.CookieContainer = cookieContainer;
                        redirectRequest.AllowAutoRedirect = false;
                        requestPostResponse = (HttpWebResponse)redirectRequest.GetResponse();
                        
                        persistCookies.Add(requestPostResponse.Cookies);
                    }
                    requestPostResponse.Close();
                }
                else {
                    CookieContainer cookieContainer = new CookieContainer();
                    foreach (Cookie cookie in persistCookies) {
                        cookieContainer.Add(cookie);
                    }
                    request.CookieContainer = cookieContainer;
                    request.GetResponse();
                }
            }

            // Stop the stopwatch, write the result to the console
            timer.Stop();
            Console.WriteLine("End Transaction # {0} of {1} - {2} took {3}ms", currentTransaction, totalTransactions, requests.testData[requests.testData.Count - 1].url, timer.ElapsedMilliseconds);
        }
    }

    public class Helpers {
        /// <summary>
        /// Generates the request data for the target url.
        /// </summary>
        /// <param name="postData">An XmlNodeList representing the data required to POST to the target url.</param>
        /// <returns>A string representing the POST data.</returns>
        public static string GenerateTargetRequestData(XmlNodeList postData) {
            StringBuilder postDataString = new StringBuilder();

            foreach (XmlNode item in postData) {
                postDataString.AppendFormat("{0}={1}&", item["name"].InnerText, item["value"].InnerText);
            }
            return postDataString.ToString().TrimEnd('&');
        }


        /// <summary>
        /// Generates the request data for the target url.
        /// </summary>
        /// <param name="postData">An XmlNodeList representing the data required to POST to the target url.</param>
        /// <returns>A string representing the POST data.</returns>
        public static string GenerateTargetRequestData(NameValueCollection postData) {

            StringBuilder postDataString = new StringBuilder();

            foreach (string key in postData.Keys) {
                postDataString.AppendFormat("{0}={1}&", key, postData[key]);
            }
            return postDataString.ToString().TrimEnd('&');
        }
    }
}
