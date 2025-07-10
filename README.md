Geo-Donation Platform
This is a full-stack web application built with ASP.NET Core MVC and the ArcGIS API for JavaScript. 
The project's goal is to provide an interactive platform that visually connects donors with charities and individual beneficiaries on a dynamic map, 
facilitating targeted and informed donations.

🚀 Key Features
Interactive Map Display: Renders all charities and needy cases as points on an ArcGIS map.

Clustering: Aggregates dense points into numbered clusters when zoomed out to improve performance and readability.

Smart Symbology: The size of each point on the map is data-driven, based on the how_much_do_you_need amount, making it easy to identify cases with the greatest need.

Enhanced Popups: Clicking any feature on the map reveals a popup with details and an "Add to Donation" button for quick actions directly from the map.

Integrated Data Table: Displays all beneficiaries in a table that is fully synced with the map.

Advanced Filtering:

Filter by beneficiary type (Charity / Needy).

Filter by field of aid (Medical, Education, etc.).

Filter to hide cases whose funding goals are complete (Needed amount is zero).

Multi-level Sorting: Allows users to sort beneficiaries by multiple criteria simultaneously (e.g., by Highest Urgency, then by Most Needed).

Client-Side Pagination: Manages large datasets efficiently by breaking the table into navigable pages.

Multi-select Tools: Users can select multiple beneficiaries at once by either drawing a polygon on the map or using table checkboxes.

Integrated Donation System: A donation summary "cart" allows users to review their selected donations before submitting them all at once.

🛠️ Technology Stack
Backend:

C#

ASP.NET Core MVC

Frontend:

HTML5 & CSS3

Bootstrap 5

JavaScript (ES6+)

jQuery

GIS:

ArcGIS API for JavaScript 4.29

ArcGIS Feature Layers

⚙️ Setup & Configuration
To run this project locally, follow these steps:

Clone Repository: Clone this repository to your local machine.

Open in Visual Studio: Open the .sln solution file in Visual Studio.

Restore Packages: Visual Studio should automatically restore all required NuGet packages upon opening the solution.

ArcGIS Configuration (Very Important):

Update Service URLs: In the Donate.cshtml file, find the NEEDIES_SERVICE_URL and CHARITIES_SERVICE_URL constants and replace the placeholder URLs with the actual URLs of your own ArcGIS Feature Layers.

Configure CORS: Log in to your ArcGIS Online account, navigate to Organization -> Settings -> Security, and find the "Allow origins" section. Add your local development URL (e.g., https://localhost:xxxx) to the list to prevent Cross-Origin Resource Sharing errors.

Enable Editing on Layers: In your ArcGIS account, ensure that your beneficiary Feature Layers have "Enable editing" turned on in their item settings. This is required for the application to update the "needed amount" after a donation is made.

Run Project: Press the run button (▶️) in Visual Studio to start the application.

📄 License
This project is licensed under the MIT License.

The MIT License (MIT)

Copyright (c) 2025 Ahmed Y-ezzat

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, 
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, 
and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

📧 Contact
Name: Ahmed Y-ezzat

Email: ahmedy1902@gmail.com

LinkedIn: https://www.linkedin.com/in/ahmed-y-ezzat/
