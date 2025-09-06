using Accounts.Services;
using Accounts.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

public class DonateController : Controller
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ArcGisService _arcGisService;
    private readonly ArcGisSettings _arcGisSettings;
    private readonly IConfiguration _config;
    private readonly ILogger<DonateController> _logger;
    private readonly HttpClient _httpClient;

    public DonateController(UserManager<IdentityUser> userManager, ArcGisService arcGisService, IOptions<ArcGisSettings> settings,
        IConfiguration config, ILogger<DonateController> logger , HttpClient httpClient)
    {
        _userManager = userManager;
        _arcGisService = arcGisService;
        _arcGisSettings = settings.Value;
        _config = config;
        _logger = logger;
        _httpClient = httpClient;
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

        string prompt = $@"
You are an intelligent assistant that extracts search filters for Egyptian donation cases.

IMPORTANT RULES:
1. If user asks for ""near me"", ""closest"", ""أقرب"", ""من مكاني"", set location to ""NEAR_ME""
2. For Egyptian governorates, use EXACT English names from this list ONLY:
   - Alexandria (الإسكندرية, اسكندرية)
   - Aswan (أسوان)
   - Asyut (أسيوط)
   - Beheira (البحيرة)
   - Beni Suef (بني سويف)
   - Cairo (القاهرة)
   - Dakahlia (الدقهلية)
   - Damietta (دمياط)
   - Faiyum (الفيوم)
   - Gharbia (الغربية)
   - Giza (الجيزة)
   - Ismailia (الإسماعيلية)
   - Kafr El Sheikh (كفر الشيخ)
   - Luxor (الأقصر)
   - Matrouh (مطروح)
   - Menofia (المنوفية)
   - Minya (المنيا)
   - New Valley (الوادي الجديد)
   - North Sinai (شمال سيناء)
   - Port Said (بورسعيد)
   - Qalyubia (القليوبية)
   - Qena (قنا)
   - Red Sea (البحر الأحمر)
   - Sharqia (الشرقية)
   - Sohag (سوهاج)
   - South Sinai (جنوب سيناء)
   - Suez (السويس)

3. For need types, use: medical, education, food, housing, clothing

4. For amounts, use proper numbers without currency symbols:
   - ""under 1000"" → amount_less_than: 1000
   - ""over 500"" → amount_greater_than: 500
   - ""اقل من 5000"" → amount_less_than: 5000
   - ""اكثر من 1000"" → amount_greater_than: 1000

EXAMPLES:
""Medical cases in Cairo under 1000"" → {{""location"": ""Cairo"", ""need_type"": ""medical"", ""amount_less_than"": 1000}}
""عايز الحالات في المنوفية اقل من 5000"" → {{""location"": ""Menofia"", ""need_type"": null, ""amount_less_than"": 5000}}
""أقرب حالات من مكاني"" → {{""location"": ""NEAR_ME"", ""need_type"": null}}
""cases in Gharbia over 500"" → {{""location"": ""Gharbia"", ""need_type"": null, ""amount_greater_than"": 500}}
""تعليم في الغربية اكثر من 2000"" → {{""location"": ""Gharbia"", ""need_type"": ""education"", ""amount_greater_than"": 2000}}

User Request: ""{userQuery}""

Return ONLY valid JSON:
{{
  ""location"": null,
  ""need_type"": null,
  ""amount_less_than"": null,
  ""amount_greater_than"": null
}}

JSON:";

        try
        {
            var requestData = new
            {
                model = "llama3:latest",
                prompt = prompt,
                stream = false,
                options = new { temperature = 0.1 }
            };

            var jsonData = JsonSerializer.Serialize(requestData);
            var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

            _logger.LogInformation($"Sending AI request for query: {userQuery}");

            var response = await _httpClient.PostAsync(ollamaUrl, content);
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"Ollama raw response: {responseContent}");

                var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseContent);
                var cleanedJson = CleanJsonResponse(ollamaResponse.response);
                _logger.LogInformation($"Cleaned JSON: {cleanedJson}");

                // ✅ استخدام دالة التحقق والتنظيف
                var validatedJson = ValidateAndCleanAIResponse(cleanedJson, userQuery);
                if (!string.IsNullOrEmpty(validatedJson))
                {
                    _logger.LogInformation($"Validated JSON: {validatedJson}");
                    _logger.LogInformation("Successfully processed AI query");
                    return Ok(validatedJson);
                }
                else
                {
                    _logger.LogWarning("AI response validation failed, returning empty filter");
                    return Ok("{}");
                }
            }
            else
            {
                _logger.LogError($"Ollama API returned error: {response.StatusCode}");
                return StatusCode(503, new { error = "AI service temporarily unavailable" });
            }
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("AI request timed out");
            return StatusCode(408, new { error = "Request timed out" });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in AI search: {ex.Message}");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
    private string CleanJsonResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return "{}";

        // Remove markdown code block markers and extra whitespace
        var cleaned = response.Trim()
            .Replace("```", "") // Fixed the issue by ensuring the string is properly closed
            .Replace("```", "")
            .Trim();

        // Find JSON object boundaries
        int startIndex = cleaned.IndexOf('{');
        int lastIndex = cleaned.LastIndexOf('}');

        if (startIndex >= 0 && lastIndex >= startIndex)
        {
            cleaned = cleaned.Substring(startIndex, lastIndex - startIndex + 1);
        }

        // Additional cleaning for text before and after JSON
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"^[^{]*", "");
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[^}]*$", "");

        return cleaned;
    }

    private string ؤ(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return "{}";

        // Remove markdown code block markers and extra whitespace
        var cleaned = response.Trim()
.Replace("```", "")
.Replace("```", "")
.Trim();

        // Find JSON object boundaries
        int startIndex = cleaned.IndexOf('{');
        int lastIndex = cleaned.LastIndexOf('}');

        if (startIndex >= 0 && lastIndex >= startIndex)
        {
            cleaned = cleaned.Substring(startIndex, lastIndex - startIndex + 1);
        }

        return cleaned;
    }

    public class OllamaResponse
    {
        public string response { get; set; }
        public bool done { get; set; }
    }


    // ✅ دالة Validation محسنة جداً
    private string ValidateAndCleanAIResponse(string aiResponse, string originalQuery)
    {
        try
        {
            _logger.LogInformation($"Validating AI response: {aiResponse}");

            var filters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(aiResponse);
            if (filters == null)
            {
                _logger.LogWarning("Failed to deserialize AI response");
                return null;
            }

            var cleanedFilters = new Dictionary<string, object>();
            bool hasValidFilter = false;

            // معالجة الموقع
            if (filters.ContainsKey("location") && filters["location"].ValueKind == JsonValueKind.String)
            {
                var location = filters["location"].GetString()?.Trim();
                if (!string.IsNullOrEmpty(location))
                {
                    if (IsNearMeRequest(location, originalQuery))
                    {
                        cleanedFilters["location"] = "NEAR_ME";
                        hasValidFilter = true;
                        _logger.LogInformation("Location set to NEAR_ME");
                    }
                    else
                    {
                        location = NormalizeEgyptianGovernorate(location);
                        if (!string.IsNullOrEmpty(location))
                        {
                            cleanedFilters["location"] = location;
                            hasValidFilter = true;
                            _logger.LogInformation($"Location normalized to: {location}");
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
                        _logger.LogInformation($"Need type set to: {needType}");
                    }
                }
            }

            // ✅ معالجة محسنة للمبالغ
            if (filters.ContainsKey("amount_less_than"))
            {
                if (filters["amount_less_than"].ValueKind == JsonValueKind.Number)
                {
                    if (filters["amount_less_than"].TryGetDecimal(out decimal lessValue) && lessValue > 0 && lessValue <= 1000000)
                    {
                        cleanedFilters["amount_less_than"] = (int)lessValue;
                        hasValidFilter = true;
                        _logger.LogInformation($"Amount less than set to: {lessValue}");
                    }
                }
                else if (filters["amount_less_than"].ValueKind == JsonValueKind.String)
                {
                    var strValue = filters["amount_less_than"].GetString();
                    if (decimal.TryParse(strValue, out decimal lessValue) && lessValue > 0 && lessValue <= 1000000)
                    {
                        cleanedFilters["amount_less_than"] = (int)lessValue;
                        hasValidFilter = true;
                        _logger.LogInformation($"Amount less than parsed from string: {lessValue}");
                    }
                }
            }

            if (filters.ContainsKey("amount_greater_than"))
            {
                if (filters["amount_greater_than"].ValueKind == JsonValueKind.Number)
                {
                    if (filters["amount_greater_than"].TryGetDecimal(out decimal greaterValue) && greaterValue >= 0 && greaterValue <= 1000000)
                    {
                        cleanedFilters["amount_greater_than"] = (int)greaterValue;
                        hasValidFilter = true;
                        _logger.LogInformation($"Amount greater than set to: {greaterValue}");
                    }
                }
                else if (filters["amount_greater_than"].ValueKind == JsonValueKind.String)
                {
                    var strValue = filters["amount_greater_than"].GetString();
                    if (decimal.TryParse(strValue, out decimal greaterValue) && greaterValue >= 0 && greaterValue <= 1000000)
                    {
                        cleanedFilters["amount_greater_than"] = (int)greaterValue;
                        hasValidFilter = true;
                        _logger.LogInformation($"Amount greater than parsed from string: {greaterValue}");
                    }
                }
            }

            // التحقق من التعارض في المبالغ
            if (cleanedFilters.ContainsKey("amount_less_than") && cleanedFilters.ContainsKey("amount_greater_than"))
            {
                var lessValue = (int)cleanedFilters["amount_less_than"];
                var greaterValue = (int)cleanedFilters["amount_greater_than"];
                if (greaterValue >= lessValue)
                {
                    _logger.LogWarning($"Conflicting amounts: greater ({greaterValue}) >= less ({lessValue}), removing both");
                    cleanedFilters.Remove("amount_less_than");
                    cleanedFilters.Remove("amount_greater_than");
                }
            }

            var result = hasValidFilter ? JsonSerializer.Serialize(cleanedFilters) : null;
            _logger.LogInformation($"Validation result: {result ?? "null"}");
            return result;
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
