using Accounts.Services;
using Accounts.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Logging;

public class DonateController : Controller
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ArcGisService _arcGisService;
    private readonly ArcGisSettings _arcGisSettings;
    private readonly IConfiguration _config;
    private readonly ILogger<DonateController> _logger;

    public DonateController(UserManager<IdentityUser> userManager, ArcGisService arcGisService, IOptions<ArcGisSettings> settings,
        IConfiguration config, ILogger<DonateController> logger)
    {
        _userManager = userManager;
        _arcGisService = arcGisService;
        _arcGisSettings = settings.Value;
        _config = config;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Roles = "Donor")]
    public IActionResult Index()
    {
        ViewData["NeediesUrl"] = _arcGisSettings.NeediesServiceUrl;
        ViewData["CharitiesUrl"] = _arcGisSettings.CharitiesServiceUrl;
        ViewData["GovernoratesUrl"] = _arcGisSettings.GovernoratesUrl;
        ViewData["DonorsUrl"] = _arcGisSettings.DonorsServiceUrl;
        ViewData["DonationsLayerUrl"] = _arcGisSettings.DonationsLayerUrl;

        ViewData["EgyptExtent"] = new
        {
            xmin = 25.0,
            ymin = 22.0,
            xmax = 36.0,
            ymax = 32.0,
            wkid = 4326
        };

        ViewData["NeedyColor"] = "#dc3545";
        ViewData["CharityColor"] = "#198754";
        ViewData["BufferArea"] = 10;

        return View("Donate", new DonateViewModel());
    }

    [HttpGet]
    [Authorize(Roles = "Donor")]
    public async Task<IActionResult> ViewDonationHistory()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var donations = await _arcGisService.GetDonationsAsync(user.Email);
        var charities = await _arcGisService.GetCharitiesAsync();
        var needies = await _arcGisService.GetNeediesAsync();

        var accounts = new List<dynamic>();
        accounts.AddRange(charities.Select(c => new { email = c.enter_your_e_mail ?? c.charity_name, name = c.charity_name, userType = "Charity", HelpFields = c.charity_sector }));
        accounts.AddRange(needies.Select(n => new { email = n.email ?? n.full_name, name = n.full_name, userType = "Beneficiary", HelpFields = n.type_of_need }));

        ViewBag.Accounts = accounts;
        return View("ViewDonationHistory", donations);
    }

    public class DonationInputModel
    {
        public int BeneficiaryId { get; set; }
        public decimal Amount { get; set; }
        public string Type { get; set; }
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> SubmitDonations([FromBody] List<DonationInputModel> donations)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var donor = await _arcGisService.GetDonorByEmailAsync(user.Email);
        if (donor == null) return Json(new { success = false, message = "Donor email not found." });

        var charities = await _arcGisService.GetCharitiesAsync();
        var needies = await _arcGisService.GetNeediesAsync();
        var results = new List<object>();
        double totalDonated = 0;

        foreach (var d in donations)
        {
            if (d.Amount <= 0) continue;
            CharityFeature charity = null;
            NeedyFeature needy = null;

            if (d.Type == "charity")
                charity = charities.FirstOrDefault(c => c.objectid == d.BeneficiaryId);
            else if (d.Type == "needy")
                needy = needies.FirstOrDefault(n => n.objectid == d.BeneficiaryId);

            if (charity == null && needy == null) continue;

            double currentNeeded = charity?.how_much_do_you_need ?? needy.how_much_do_you_need ?? 0;
            double donationAmount = Math.Min((double)d.Amount, currentNeeded);
            if (donationAmount <= 0) continue;

            var donation = new DonationFeature
            {
                donor_email = user.Email,
                donor_x = donor.x ?? 0,
                donor_y = donor.y ?? 0,
                donation_amount = donationAmount,
                donation_date = DateTime.UtcNow,
                recipient_email = (charity != null) ? charity.enter_your_e_mail : needy.email,
                recipient_name = (charity != null) ? charity.charity_name : needy.full_name,
                donation_field = (charity != null) ? charity.charity_sector : needy.type_of_need,
                recipient_x = charity?.x ?? needy?.x ?? 0,
                recipient_y = charity?.y ?? needy?.y ?? 0
            };

            var added = await _arcGisService.AddDonationAsync(donation);
            if (added)
            {
                totalDonated += donationAmount;
                results.Add(new { id = d.BeneficiaryId, actualAmount = donationAmount });
                double newNeeded = currentNeeded - donationAmount;
                if (charity != null && !string.IsNullOrEmpty(charity.enter_your_e_mail))
                    await _arcGisService.UpdateCharityByEmailAsync(charity.enter_your_e_mail, newNeeded);
                else if (needy != null && !string.IsNullOrEmpty(needy.email))
                    await _arcGisService.UpdateNeedyByEmailAsync(needy.email, newNeeded);
            }
        }
        return Json(new { success = true, updated = results, totalDonated });
    }

    public class OllamaResponse
    {
        public string response { get; set; }
    }

    [HttpPost]
    [Authorize(Roles = "Donor")]
    public async Task<IActionResult> SearchByAI([FromBody] string userQuery)
    {
        if (string.IsNullOrWhiteSpace(userQuery))
            return BadRequest("Query cannot be empty.");

        userQuery = userQuery.Trim();
        if (userQuery.Length > 500)
            return BadRequest("Query is too long. Please keep it under 500 characters.");

        string ollamaUrl = "http://localhost:11434/api/generate";

        // ✅ Prompt محسن جداً مع أمثلة أكثر ووضوح أكبر
        string prompt = $@"
You are an intelligent assistant that extracts search filters for Egyptian donation cases.

IMPORTANT RULES:
1. If user asks for ""near me"", ""closest"", ""أقرب"", ""من مكاني"", set location to ""NEAR_ME""
2. For Egyptian governorates, use EXACT names from this list:
   - Al Qahirah (القاهرة, Cairo)
   - Al Jizah (الجيزة, Giza)  
   - Al Iskandariyah (الإسكندرية, Alexandria)
   - Al Minufiyah (المنوفية, Menoufia)
   - Al Qalyubiyah (القليوبية, Qalyubia)
   - Al Gharbiyah (الغربية, Gharbia)
   - Ad Daqahliyah (الدقهلية, Dakahlia)
   - Ash Sharqiyah (الشرقية, Sharqia)
   - Al Buhayrah (البحيرة, Beheira)
   - Kafr ash Shaykh (كفر الشيخ)
   - Dumyat (دمياط, Damietta)
   - Al Isma`iliyah (الإسماعيلية, Ismailia)
   - As Suways (السويس, Suez)
   - Bur Sa`id (بورسعيد, Port Said)

3. For need types, use: medical, education, food, housing, clothing

EXAMPLES:
""Medical cases in Cairo"" → {{""location"": ""Al Qahirah"", ""need_type"": ""medical""}}
""عايز الحالات في المنوفية"" → {{""location"": ""Al Minufiyah"", ""need_type"": null}}
""أقرب حالات من مكاني"" → {{""location"": ""NEAR_ME"", ""need_type"": null}}
""find nearest cases"" → {{""location"": ""NEAR_ME"", ""need_type"": null}}
""education under 1000"" → {{""need_type"": ""education"", ""amount_less_than"": 1000}}

User Request: ""{userQuery}""

Return ONLY valid JSON:
{{
  ""location"": null,
  ""need_type"": null,
  ""amount_less_than"": null,
  ""amount_greater_than"": null
}}

JSON:";

        var payload = new
        {
            model = "llama3:latest",
            prompt = prompt,
            format = "json",
            stream = false,
            options = new
            {
                temperature = 0.1,
                top_p = 0.8,
                num_predict = 100,
stop = new[] { "\n\n", "}" + "\n", "```" }
            }
            };
        try
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                var jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                _logger.LogInformation("Sending AI request for query: {query}", userQuery);

                var response = await httpClient.PostAsync(ollamaUrl, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("Ollama raw response: {response}", responseBody);

                if (response.IsSuccessStatusCode)
                {
                    var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseBody);

                    if (!string.IsNullOrEmpty(ollamaResponse?.response))
                    {
                        string cleanJson = CleanJsonResponse(ollamaResponse.response);
                        _logger.LogInformation("Cleaned JSON: {json}", cleanJson);

                        var validatedResponse = ValidateAndCleanAIResponse(cleanJson, userQuery);

                        if (validatedResponse != null)
                        {
                            _logger.LogInformation("Successfully processed AI query");
                            return Ok(validatedResponse);
                        }
                        else
                        {
                            _logger.LogWarning("AI response validation failed for query: {query}", userQuery);
                            return BadRequest("Could not extract valid search criteria from your request.");
                        }
                    }
                    return BadRequest("AI returned empty response");
                }
                else
                {
                    _logger.LogError("Ollama error: {status} - {error}", response.StatusCode, responseBody);
                    return StatusCode(500, $"AI service error: {response.StatusCode}");
                }
            }
        }
        catch (TaskCanceledException)
        {
            return StatusCode(408, "AI request timed out");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Cannot connect to Ollama");
            return StatusCode(503, "AI service unavailable");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected AI error");
            return StatusCode(500, "Internal AI error");
        }
    }

    // ✅ دالة تنظيف الـ JSON محسنة
    private string CleanJsonResponse(string response)
    {
        if (string.IsNullOrEmpty(response)) return response;

        response = response.Trim();

        // Remove any comments or extra text
        if (response.Contains("```json"))
        {
            int startIndex = response.IndexOf("```json") + 7; // Skip the "```json" marker
            response = response.Substring(startIndex);

            if (response.Contains("```"))
            {
                int endIndex = response.IndexOf("```");
                response = response.Substring(0, endIndex);
            }
        }

        // Find the JSON boundaries
        int jsonStart = response.IndexOf('{');
        int jsonEnd = response.LastIndexOf('}');

        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            response = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
        }

        return response.Trim();
    }

    // ✅ دالة Validation محسنة جداً
    private string ValidateAndCleanAIResponse(string aiResponse, string originalQuery)
    {
        try
        {
            var filters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(aiResponse);
            if (filters == null) return null;

            var cleanedFilters = new Dictionary<string, object>();
            bool hasValidFilter = false;

            // ✅ معالجة خاصة لطلبات "Near Me"
            if (filters.ContainsKey("location") && filters["location"].ValueKind == JsonValueKind.String)
            {
                var location = filters["location"].GetString()?.Trim();
                if (!string.IsNullOrEmpty(location))
                {
                    // التحقق من طلبات "Near Me"
                    if (IsNearMeRequest(location, originalQuery))
                    {
                        cleanedFilters["location"] = "NEAR_ME";
                        hasValidFilter = true;
                    }
                    else
                    {
                        // تطبيع أسماء المحافظات
                        location = NormalizeEgyptianGovernorate(location);
                        if (!string.IsNullOrEmpty(location))
                        {
                            cleanedFilters["location"] = location;
                            hasValidFilter = true;
                        }
                    }
                }
            }

            // معالجة نوع الحاجة
            if (filters.ContainsKey("need_type") && filters["need_type"].ValueKind == JsonValueKind.String)
            {
                var needType = filters["need_type"].GetString()?.Trim();
                if (!string.IsNullOrEmpty(needType) && !IsJunkWord(needType))
                {
                    needType = NormalizeNeedType(needType);
                    if (!string.IsNullOrEmpty(needType))
                    {
                        cleanedFilters["need_type"] = needType;
                        hasValidFilter = true;
                    }
                }
            }

            // معالجة المبالغ
            if (filters.ContainsKey("amount_less_than") && filters["amount_less_than"].ValueKind == JsonValueKind.Number)
            {
                if (filters["amount_less_than"].TryGetDecimal(out decimal lessValue) && lessValue > 0 && lessValue <= 1000000)
                {
                    cleanedFilters["amount_less_than"] = lessValue;
                    hasValidFilter = true;
                }
            }

            if (filters.ContainsKey("amount_greater_than") && filters["amount_greater_than"].ValueKind == JsonValueKind.Number)
            {
                if (filters["amount_greater_than"].TryGetDecimal(out decimal greaterValue) && greaterValue >= 0 && greaterValue <= 1000000)
                {
                    cleanedFilters["amount_greater_than"] = greaterValue;
                    hasValidFilter = true;
                }
            }

            // التحقق من التعارض في المبالغ
            if (cleanedFilters.ContainsKey("amount_less_than") && cleanedFilters.ContainsKey("amount_greater_than"))
            {
                var lessValue = (decimal)cleanedFilters["amount_less_than"];
                var greaterValue = (decimal)cleanedFilters["amount_greater_than"];
                if (greaterValue >= lessValue)
                {
                    cleanedFilters.Remove("amount_less_than");
                    cleanedFilters.Remove("amount_greater_than");
                }
            }

            return hasValidFilter ? JsonSerializer.Serialize(cleanedFilters) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating AI response");
            return null;
        }
    }

    // ✅ دالة التحقق من طلبات "Near Me"
    private bool IsNearMeRequest(string location, string originalQuery)
    {
        var nearMeKeywords = new[] {
            "near_me", "near me", "closest", "nearest", "my location", "مكاني", "أقرب", "قريب", "عايز اقرب"
        };

        return nearMeKeywords.Any(keyword =>
            location.ToLower().Contains(keyword.ToLower()) ||
            originalQuery.ToLower().Contains(keyword.ToLower())
        );
    }

    // ✅ دالة التحقق من الكلمات غير المفيدة
    private bool IsJunkWord(string word)
    {
        var junkWords = new[] { "عايز", "أريد", "want", "need", "find", "show", "get" };
        return junkWords.Contains(word.ToLower());
    }

    // ✅ تحديث دالة تطبيع المحافظات
    private string NormalizeEgyptianGovernorate(string location)
    {
        var governorateMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // القاهرة
            { "cairo", "Al Qahirah" }, { "القاهرة", "Al Qahirah" }, { "قاهرة", "Al Qahirah" },
            
            // الجيزة  
            { "giza", "Al Jizah" }, { "الجيزة", "Al Jizah" }, { "جيزة", "Al Jizah" },
            
            // الإسكندرية
            { "alexandria", "Al Iskandariyah" }, { "الإسكندرية", "Al Iskandariyah" }, { "إسكندرية", "Al Iskandariyah" },
            
            // المنوفية - هنا المشكلة كانت!
            { "menoufia", "Al Minufiyah" }, { "المنوفية", "Al Minufiyah" }, { "منوفية", "Al Minufiyah" },
            { "minufiyah", "Al Minufiyah" }, { "محافظة المنوفية", "Al Minufiyah" },
            
            // القليوبية
            { "qalyubia", "Al Qalyubiyah" }, { "القليوبية", "Al Qalyubiyah" }, { "قليوبية", "Al Qalyubiyah" },
            
            // الغربية
            { "gharbia", "Al Gharbiyah" }, { "الغربية", "Al Gharbiyah" }, { "غربية", "Al Gharbiyah" },
            
            // الدقهلية
            { "dakahlia", "Ad Daqahliyah" }, { "الدقهلية", "Ad Daqahliyah" }, { "دقهلية", "Ad Daqahliyah" },
            
            // الشرقية
            { "sharqia", "Ash Sharqiyah" }, { "الشرقية", "Ash Sharqiyah" }, { "شرقية", "Ash Sharqiyah" },
            
            // البحيرة
            { "beheira", "Al Buhayrah" }, { "البحيرة", "Al Buhayrah" }, { "بحيرة", "Al Buhayrah" },
            
            // كفر الشيخ
            { "kafr el sheikh", "Kafr ash Shaykh" }, { "كفر الشيخ", "Kafr ash Shaykh" },
            
            // دمياط
            { "damietta", "Dumyat" }, { "دمياط", "Dumyat" },
            
            // الإسماعيلية
            { "ismailia", "Al Isma`iliyah" }, { "الإسماعيلية", "Al Isma`iliyah" }, { "إسماعيلية", "Al Isma`iliyah" },
            
            // السويس
            { "suez", "As Suways" }, { "السويس", "As Suways" }, { "سويس", "As Suways" },
            
            // بورسعيد
            { "port said", "Bur Sa`id" }, { "بورسعيد", "Bur Sa`id" },

            // أسوان
            { "aswan", "Aswan" }, { "أسوان", "Aswan" },
            
            // الأقصر
            { "luxor", "Luxor" }, { "الأقصر", "Luxor" }, { "اقصر", "Luxor" },
            
            // أسيوط
            { "asyut", "Asyut" }, { "أسيوط", "Asyut" }, { "اسيوط", "Asyut" },
            
            // سوهاج
            { "sohag", "Suhaj" }, { "سوهاج", "Suhaj" },
            
            // قنا
            { "qena", "Qina" }, { "قنا", "Qina" },
            
            // البحر الأحمر
            { "red sea", "Al Bahr al Ahmar" }, { "البحر الأحمر", "Al Bahr al Ahmar" },
            
            // الوادي الجديد
            { "new valley", "Al Wadi at Jadid" }, { "الوادي الجديد", "Al Wadi at Jadid" },
            
            // مطروح
            { "matrouh", "Matruh" }, { "مطروح", "Matruh" },
            
            // شمال سيناء
            { "north sinai", "Shamal Sina'" }, { "شمال سيناء", "Shamal Sina'" },
            
            // جنوب سيناء
            { "south sinai", "Janub Sina'" }, { "جنوب سيناء", "Janub Sina'" },
            
            // بني سويف
            { "beni suef", "Bani Suwayf" }, { "بني سويف", "Bani Suwayf" },
            
            // الفيوم
            { "fayoum", "Al Fayyum" }, { "الفيوم", "Al Fayyum" },
            
            // المنيا
            { "minya", "Al Minya" }, { "المنيا", "Al Minya" }
        };

        return governorateMapping.TryGetValue(location, out string normalized) ? normalized : null;
    }

    private string NormalizeNeedType(string needType)
    {
        var needTypeMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "medical", "medical" }, { "طبي", "medical" }, { "طبية", "medical" },
            { "صحي", "medical" }, { "علاج", "medical" }, { "صحة", "medical" },

            { "education", "education" }, { "تعليم", "education" }, { "تعليمي", "education" },
            { "دراسة", "education" }, { "مدرسة", "education" }, { "جامعة", "education" },

            { "food", "food" }, { "طعام", "food" }, { "غذاء", "food" },
            { "أكل", "food" }, { "وجبات", "food" },

            { "housing", "housing" }, { "سكن", "housing" }, { "إسكان", "housing" },
            { "مأوى", "housing" }, { "منزل", "housing" },

            { "clothing", "clothing" }, { "ملابس", "clothing" }, { "كساء", "clothing" }
        };

        return needTypeMapping.TryGetValue(needType, out string normalized) ? normalized : needType;
    }
}
