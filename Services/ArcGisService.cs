using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Accounts.Services
{
    public class ArcGisResponse<T> { public List<ArcGisFeature<T>> features { get; set; } }
    public class ArcGisFeature<T> { public T attributes { get; set; } public Geometry geometry { get; set; } }
    public class Geometry { public double x { get; set; } public double y { get; set; } }

    public class CharityFeature
    {
        public int? objectid { get; set; }
        public string charity_name { get; set; }
        public string charity_sector { get; set; }
        public double? how_much_do_you_need { get; set; }
        public double? x { get; set; } // Longitude
        public double? y { get; set; } // Latitude
    }
    public class NeedyFeature
    {
        public int? objectid { get; set; }
        public string full_name { get; set; }
        public string type_of_need { get; set; }
        public double? how_much_do_you_need { get; set; }
        public double? x { get; set; } // Longitude
        public double? y { get; set; } // Latitude
    }
    public class DonationFeature
    {
        public string donor_name { get; set; }
        public string recipient_name { get; set; }
        public string donation_field { get; set; }
        public double donation_amount { get; set; }
        public DateTime donation_date { get; set; }
        public double donor_x { get; set; }
        public double donor_y { get; set; }
        public double recipient_x { get; set; }
        public double recipient_y { get; set; }
    }

    public class ArcGisDonation
    {
        public string donor_name { get; set; }
        public string recipient_name { get; set; }
        public string donation_field { get; set; }
        public double? donation_amount { get; set; }
        public DateTime? donation_date { get; set; }
        public double? donor_x { get; set; }
        public double? donor_y { get; set; }
        public double? recipient_x { get; set; }
        public double? recipient_y { get; set; }
    }

    public class ArcGisService
    {
        private readonly HttpClient _client;
        public ArcGisService(HttpClient client)
        {
            _client = client;
        }

        public async Task<List<CharityFeature>> GetCharitiesAsync()
        {
            var url = "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_2c36d5ade9064fe685d54893df3b37ea_results/FeatureServer/0/query?where=1=1&outFields=*&returnGeometry=true&f=json";
            var response = await _client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<ArcGisResponse<CharityFeature>>(response);
            return data?.features?.Select(f => {
                f.attributes.x = f.geometry?.x;
                f.attributes.y = f.geometry?.y;
                return f.attributes;
            }).ToList() ?? new();
        }

        public async Task<List<NeedyFeature>> GetNeediesAsync()
        {
            var url = "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_1b6326b33d2b4213bf757d6780a0f12a_results/FeatureServer/0/query?where=1=1&outFields=*&returnGeometry=true&f=json";
            var response = await _client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<ArcGisResponse<NeedyFeature>>(response);
            return data?.features?.Select(f => {
                f.attributes.x = f.geometry?.x;
                f.attributes.y = f.geometry?.y;
                return f.attributes;
            }).ToList() ?? new();
        }

        public async Task<bool> AddDonationAsync(DonationFeature donation)
        {
            var url = "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/Donations_made_by_donors/FeatureServer/0/addFeatures";
            var features = new[]
            {
                new
                {
                    attributes = new {
                        donor_name = donation.donor_name,
                        recipient_name = donation.recipient_name,
                        donation_field = donation.donation_field,
                        donation_amount = donation.donation_amount,
                        donation_date = donation.donation_date,
                        donor_x = donation.donor_x,
                        donor_y = donation.donor_y,
                        recipient_x = donation.recipient_x,
                        recipient_y = donation.recipient_y
                    },
                    geometry = new { x = donation.donor_x, y = donation.donor_y, spatialReference = new { wkid = 4326 } }
                }
            };
            var data = new { features, f = "json" };
            var json = JsonSerializer.Serialize(data);
            var response = await _client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
            var responseContent = await response.Content.ReadAsStringAsync();
            // Ì„ﬂ‰ﬂ ÿ»«⁄… responseContent ··œÌ»«ÃÂ
            return response.IsSuccessStatusCode && responseContent.Contains("success");
        }

        public async Task<List<ArcGisDonation>> GetDonationsAsync(string donorEmail)
        {
            var url = $"https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/Donations_made_by_donors/FeatureServer/0/query?where=donor_name='{donorEmail}'&outFields=*&f=json";
            var response = await _client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<ArcGisResponse<ArcGisDonation>>(response);
            return data?.features?.Select(f => f.attributes).ToList() ?? new();
        }

        public async Task<CharityFeature> GetDonorByEmailAsync(string email)
        {
            var url = $"https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_fb464f56faae4b6c803825277c69be1c_results/FeatureServer/0/query?where=enter_your_e_mail='{email}'&outFields=*&returnGeometry=true&f=json";
            var response = await _client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<ArcGisResponse<CharityFeature>>(response);
            return data?.features?.Select(f => {
                f.attributes.x = f.geometry?.x;
                f.attributes.y = f.geometry?.y;
                return f.attributes;
            }).FirstOrDefault();
        }
    }
}
