using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace HttpGrabberFunctions
{
    public static class HttpHelper
    {
        private static HttpClient client = new HttpClient();

        public static async Task<string> MakeGetRequest(string url)
        {
            try
            {
                //asynchronously makes a get request to the link we want to
                HttpResponseMessage response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                //stringfy the response
                string responseBody = await response.Content.ReadAsStringAsync();
                return responseBody;
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine("\nException Caught!");
                Console.WriteLine("Message :{0} ", e.Message);
                throw;
            }

        }
        public static async Task<string> MakeAuthenticatedGetRequest(string url, string Token)
        {
            try
            {
                //Sets securely our canvas token to our http header
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
                //asynchronously makes a get request to the link we want to
                HttpResponseMessage response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                //stringfy the response
                string responseBody = await response.Content.ReadAsStringAsync();
                return responseBody;
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine("\nException Caught!");
                Console.WriteLine("Message :{0} ", e.Message);
                throw;
            }

        }
    }
    public abstract class DataGrabber
    {

        private string _URL;
        public string URL
        {
            get
            {
                return _URL;
            }
            set
            {
                _URL = value;
            }
        }
        public DataGrabber() { }
        public DataGrabber(string URL)
        {
            System.Console.WriteLine("Running Base!");
            this.URL = URL;
        }

        public virtual async Task<string> GetResponse()
        {
            string result = "";
            try
            {
                if (_URL == null)
                    throw new Exception("No URL was specififed");
                result = await HttpHelper.MakeGetRequest(_URL);
            }
            catch (Exception e)
            {
                System.Console.WriteLine(e.Message);
                result = "Error!";
            }
            return result;
        }
        public virtual async Task<string> GetAuthResponse(string token)
        {
            string result = "";
            try
            {
                if (_URL == null)
                    throw new Exception("No URL was specififed");
                result = await HttpHelper.MakeAuthenticatedGetRequest(_URL, token);
            }
            catch (Exception e)
            {
                System.Console.WriteLine(e.Message);
                result = "Error!";
            }
            return result;
        }
    }
    public class CanvasGrabber : DataGrabber
    {
        public CanvasGrabber() : base()
        {

        }
        private static string TurnToQuery(string text)
        {
            return string.Join("_", text.Split(" ").Select(item => item.ToLower()));
        }
        public CanvasGrabber(string apipath) : base("https://byui.instructure.com" + apipath)
        {
            System.Console.WriteLine("Running Inherited!");
            System.Console.WriteLine(this.URL);
        }

    }
}
