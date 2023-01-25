using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
namespace SSLProxyServer;

public sealed class Server
{
    private static readonly int BUFFER_SIZE = 8192;                   //buffer size for flow reading
    private static readonly string _address = "127.0.0.1";            //local IP address
    private static readonly int _port = 8888;                         //Port on which the proxy will run
    private static  TcpListener _listener;                            //listener
    private static X509Certificate2 _certificate;                     //SSL Certificate
    private static readonly char[] spaceSplit = { ' ' };              //Distance of separation
    private static readonly char[] semiSplit = { ';' };               //Separation symbol
    private static readonly string[] colonSpaceSplit = { ": " };      //Column separation symbol

    //Get a proxy server object
    public static Server Proxy { get ; } = new();

    private Server()
    {
        //инициализируется прослушиватель
        _listener = new TcpListener(IPAddress.Parse(_address), _port);
        ServicePointManager.ServerCertificateValidationCallback = delegate
        {
            return true;
        };
    }

    //proxy launch
    public void Start()
    {
        try
        {
            //get the certificate from the root authority
            var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            _certificate = store.Certificates.First(c => c.FriendlyName == "habr.com");
            _listener.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to obtain SSL certificate {ex.Message}");
        }

        Listen(_listener);
    }

    /// <summary>
    /// Let's start listening
    /// </summary>
    private static void Listen(TcpListener listener)
    {
        try
        {
            while (true)
            {
                // we are waiting for requests from the client
                if (listener.Pending())
                {
                    // create a flow
                    var t = new Thread(RequestProcessing)
                    {
                        IsBackground = true
                    };
                    t.Start(listener.AcceptTcpClient());
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error listening {ex.Message}");
        }
    }

    /// <summary>
    /// request processing
    /// </summary>
    private static void RequestProcessing(object obj)
    {
        var client = (TcpClient)obj;

        try
        {
            RequestHandler(client);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        finally
        {
            client.Close();
        }
    }

    /// <summary>
    /// Request Handler
    /// </summary>
    private static void RequestHandler(TcpClient client)
    {
        Console.WriteLine("Поступил запрос на обработку");
        //получаем поток от клиента
        Stream clientStream = client.GetStream();
        var outStream = clientStream;
        SslStream sslStream = null;
        var clientStreamReader = new StreamReader(clientStream);
        var connectStreamWriter = new StreamWriter(clientStream);

        try
        {
            //read the first http command
            var httpCmd = clientStreamReader.ReadLine();

            if (string.IsNullOrEmpty(httpCmd))
            {
                clientStreamReader.Close();
                clientStream.Close();

                return;
            }

            //break up the line into three components
            var splitBuffer = httpCmd.Split(spaceSplit, 3);

            var method = splitBuffer[0];
            var remoteUri = splitBuffer[1];
            var version = new Version(1, 0);

            HttpWebResponse response;

            if (splitBuffer[0].ToUpper() == "CONNECT")
            {
                remoteUri = "https://" + splitBuffer[1];

                while (!string.IsNullOrEmpty(clientStreamReader.ReadLine())) { }

                connectStreamWriter.WriteLine("HTTP/1.0 200 Connection established");
                connectStreamWriter.WriteLine($"Timestamp: {DateTime.Now.ToString()}");
                connectStreamWriter.WriteLine();

                connectStreamWriter.Flush();
                sslStream = new SslStream(clientStream, false);
                sslStream.AuthenticateAsServer(_certificate, false, SslProtocols.Tls12, true);

                //HTTPS server created - we can now decrypt the client's traffic
                clientStream = sslStream;
                clientStreamReader = new StreamReader(sslStream);
                outStream = sslStream;

                //read the new http command.
                httpCmd = clientStreamReader.ReadLine();

                if (string.IsNullOrEmpty(httpCmd))
                {
                    clientStreamReader.Close();
                    clientStream.Close();
                    sslStream.Close();

                    return;
                }
                splitBuffer = httpCmd.Split(spaceSplit, 3);
                method = splitBuffer[0];
                remoteUri += splitBuffer[1];
            }

            var webReq = (HttpWebRequest)WebRequest.Create(remoteUri);
            webReq.Method = method;
            webReq.ProtocolVersion = version;


            //create a web request that we are going to issue on behalf of the client.
            var contentLen = ReadRequestHeaders(clientStreamReader, webReq);

            webReq.Proxy = null;
            webReq.KeepAlive = false;
            webReq.AllowAutoRedirect = false;
            webReq.AutomaticDecompression = DecompressionMethods.None;

            if (method.ToUpper() == "POST")
            {
                var postBuffer = new char[contentLen];
                int bytesRead;
                var totalBytesRead = 0;
                var sw = new StreamWriter(webReq.GetRequestStream());

                while (totalBytesRead < contentLen && (bytesRead = clientStreamReader.ReadBlock(postBuffer, 0, contentLen)) > 0)
                {
                    totalBytesRead += bytesRead;
                    sw.Write(postBuffer, 0, bytesRead);
                }

                sw.Close();
            }

            webReq.Timeout = 15000;

            try
            {
                response = (HttpWebResponse)webReq.GetResponse();
            }
            catch (WebException webEx)
            {
                response = webEx.Response as HttpWebResponse;
            }

            if (response != null)
            {
                var responseHeaders = GetResponseHeaders(response);
                var myResponseWriter = new StreamWriter(outStream);
                var responseStream = response.GetResponseStream() ?? throw new Exception("Не удалось получить ответ");

                try
                {
                    //send the response status and response headers
                    WriteResponseStatus(response.StatusCode, response.StatusDescription, myResponseWriter);
                    WriteResponseHeaders(myResponseWriter, responseHeaders);

                    var buffer = response.ContentLength > 0 ? new byte[response.ContentLength] : new byte[BUFFER_SIZE];

                    int bytesRead;


                    while ((bytesRead = responseStream.Read(buffer, 0, buffer.Length)) > 0)
                        outStream.Write(buffer, 0, bytesRead);

                    responseStream.Close();
                    outStream.Flush();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
                finally
                {
                    responseStream.Close();
                    response.Close();
                    myResponseWriter.Close();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        finally
        {
            connectStreamWriter.Close();
            clientStreamReader.Close();
            clientStream.Close();
            sslStream?.Close();
            outStream.Close();
        }
    }

    /// <summary>
    /// Reading the headings
    /// </summary>
    private static int ReadRequestHeaders(StreamReader sr, HttpWebRequest webReq)
    {
        string httpCmd;
        var contentLen = 0;

        do
        {
            httpCmd = sr.ReadLine();

            if (string.IsNullOrEmpty(httpCmd))
                return contentLen;

            var header = httpCmd.Split(colonSpaceSplit, 2, StringSplitOptions.None);

            switch (header[0].ToLower())
            {
                case "host":
                    webReq.Host = header[1];

                    break;
                case "user-agent":
                    webReq.UserAgent = header[1];

                    break;
                case "accept":
                    webReq.Accept = header[1];

                    break;
                case "referer":
                    webReq.Referer = header[1];

                    break;
                case "cookie":
                    webReq.Headers["Cookie"] = header[1];

                    break;
                case "proxy-connection":
                case "connection":
                case "keep-alive":
                    //ignore these
                    break;
                case "content-length":
                    int.TryParse(header[1], out contentLen);

                    break;
                case "content-type":
                    webReq.ContentType = header[1];

                    break;
                case "if-modified-since":
                    var sb = header[1].Trim().Split(semiSplit);
                    if (DateTime.TryParse(sb[0], out var d))
                        webReq.IfModifiedSince = d;

                    break;
                default:
                    try
                    {
                        webReq.Headers.Add(header[0], header[1]);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Не удалось добавить заголовок {header[0]}. {ex.Message}");
                    }

                    break;
            }
        } while (!string.IsNullOrWhiteSpace(httpCmd));

        return contentLen;
    }

    /// <summary>
    /// Response handler
    /// </summary>
    private static List<Tuple<string, string>> GetResponseHeaders(HttpWebResponse response)
    {
        var returnHeaders = new List<Tuple<string, string>>();

        foreach (string s in response.Headers.Keys)
            returnHeaders.Add(new Tuple<string, string>(s, response.Headers[s]));

        returnHeaders.Add(new Tuple<string, string>("X-Proxied-By", "askhat proxy"));

        return returnHeaders;
    }

    /// <summary>
    /// Record the status of the response
    /// </summary>
    private static void WriteResponseStatus(HttpStatusCode code, string description, StreamWriter myResponseWriter)
    {
        var s = $"HTTP/1.0 {(int)code} {description}";
        myResponseWriter.WriteLine(s);
    }

    /// <summary>
    /// Writing down the response headers
    /// </summary>
    private static void WriteResponseHeaders(StreamWriter myResponseWriter, List<Tuple<string, string>> headers)
    {
        if (headers != null)
            foreach (var header in headers)
                myResponseWriter.WriteLine($"{header.Item1}: {header.Item2}");
        myResponseWriter.WriteLine();
        myResponseWriter.Flush();
    }
}