using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SentinelMap.Infrastructure.Data;

namespace SentinelMap.Api.Endpoints;

public static class EntityEndpoints
{
    public static void MapEntityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/entities").WithTags("Entities");

        group.MapGet("/{id:guid}", GetById).RequireAuthorization("ViewerAccess");
        // Track history endpoint is in TrackEndpoints.cs
    }

    private static async Task<IResult> GetById(
        Guid id,
        SentinelMapDbContext db,
        CancellationToken ct)
    {
        var entity = await db.Entities
            .Include(e => e.Identifiers)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        if (entity is null) return Results.NotFound();

        // Get latest observation with RawData for enrichment
        var latestObs = await db.Observations
            .Where(o => o.EntityId == id && o.RawData != null)
            .OrderByDescending(o => o.ObservedAt)
            .FirstOrDefaultAsync(ct);

        var enrichment = BuildEnrichment(entity.Type.ToString(), entity.Identifiers, latestObs?.RawData);

        var speedKnots = entity.LastSpeedMps.HasValue
            ? Math.Round(entity.LastSpeedMps.Value * 1.94384, 1)
            : (double?)null;

        return Results.Ok(new
        {
            entity.Id,
            Type = entity.Type.ToString(),
            entity.DisplayName,
            Status = entity.Status.ToString(),
            LastKnownPosition = entity.LastKnownPosition is not null
                ? new { Longitude = entity.LastKnownPosition.X, Latitude = entity.LastKnownPosition.Y }
                : null,
            LastSpeedKnots = speedKnots,
            entity.LastHeading,
            entity.LastSeen,
            Identifiers = entity.Identifiers.Select(i => new
            {
                Type = i.IdentifierType,
                Value = i.IdentifierValue,
                i.Source
            }),
            Enrichment = enrichment
        });
    }

    private static async Task<IResult> GetTrack(
        Guid id,
        SentinelMapDbContext db,
        int hours = 24,
        CancellationToken ct = default)
    {
        var since = DateTimeOffset.UtcNow.AddHours(-hours);

        var positions = await db.Observations
            .Where(o => o.EntityId == id && o.ObservedAt >= since && o.Position != null)
            .OrderBy(o => o.ObservedAt)
            .Select(o => new
            {
                Longitude = o.Position!.X,
                Latitude = o.Position!.Y,
                o.SpeedMps,
                o.Heading,
                o.ObservedAt
            })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            EntityId = id,
            Since = since,
            Points = positions.Select(p => new
            {
                p.Longitude,
                p.Latitude,
                SpeedKnots = p.SpeedMps.HasValue ? Math.Round(p.SpeedMps.Value * 1.94384, 1) : (double?)null,
                p.Heading,
                Timestamp = p.ObservedAt
            })
        });
    }

    private static object BuildEnrichment(
        string entityType,
        IEnumerable<Domain.Entities.EntityIdentifier> identifiers,
        string? rawDataJson)
    {
        string? vesselType = null;
        string? aircraftType = null;
        string? flag = null;
        string? imo = null;
        string? callsign = null;
        string? registration = null;
        string? squawk = null;
        double? altitude = null;
        string? photoUrl = null;
        string? externalUrl = null;

        // Try to extract enrichment from RawData JSON
        if (!string.IsNullOrEmpty(rawDataJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(rawDataJson);
                var root = doc.RootElement;

                vesselType = TryGetString(root, "ShipType") ?? TryGetString(root, "shipType") ?? TryGetString(root, "vesselType");
                callsign = TryGetString(root, "CallSign") ?? TryGetString(root, "callSign") ?? TryGetString(root, "callsign");
                imo = TryGetString(root, "ImoNumber") ?? TryGetString(root, "imoNumber") ?? TryGetString(root, "imo");
                registration = TryGetString(root, "Registration") ?? TryGetString(root, "registration");
                squawk = TryGetString(root, "Squawk") ?? TryGetString(root, "squawk");
                aircraftType = TryGetString(root, "AircraftType") ?? TryGetString(root, "aircraftType") ?? TryGetString(root, "category");

                if (root.TryGetProperty("Altitude", out var alt) || root.TryGetProperty("altitude", out alt) || root.TryGetProperty("alt", out alt))
                {
                    if (alt.ValueKind == JsonValueKind.Number)
                        altitude = alt.GetDouble();
                }
            }
            catch
            {
                // If RawData is malformed, skip enrichment
            }
        }

        // Build external URLs and flag from identifiers
        var mmsi = identifiers.FirstOrDefault(i =>
            i.IdentifierType.Equals("MMSI", StringComparison.OrdinalIgnoreCase))?.IdentifierValue;

        var icao = identifiers.FirstOrDefault(i =>
            i.IdentifierType.Equals("ICAO", StringComparison.OrdinalIgnoreCase))?.IdentifierValue;

        if (entityType == "Vessel" && mmsi is not null)
        {
            flag = GetFlagFromMmsi(mmsi);
            externalUrl = $"https://www.marinetraffic.com/en/ais/details/ships/mmsi:{mmsi}";
        }

        if (entityType == "Aircraft")
        {
            if (registration is not null)
            {
                photoUrl = $"https://www.planespotters.net/photos/reg/{registration}";
                externalUrl = photoUrl;
            }
            else if (icao is not null)
            {
                externalUrl = $"https://www.planespotters.net/hex/{icao.ToUpperInvariant()}";
            }
        }

        return new
        {
            VesselType = vesselType,
            AircraftType = aircraftType,
            PhotoUrl = photoUrl,
            ExternalUrl = externalUrl,
            Flag = flag,
            Imo = imo,
            Callsign = callsign,
            Registration = registration,
            Squawk = squawk,
            Altitude = altitude
        };
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            var val = prop.GetString();
            return string.IsNullOrWhiteSpace(val) ? null : val;
        }
        return null;
    }

    private static string? GetFlagFromMmsi(string mmsi)
    {
        if (mmsi.Length < 3) return null;
        return mmsi[..3] switch
        {
            "201" => "Albania", "211" => "Germany", "226" or "227" or "228" => "France",
            "230" => "Finland", "231" => "Faroe Islands", "232" or "233" or "234" or "235" => "United Kingdom",
            "236" => "Gibraltar", "237" => "Greece", "238" => "Croatia",
            "240" => "Greece", "241" => "Greece", "244" or "245" or "246" => "Netherlands",
            "247" or "248" => "Italy", "249" or "250" => "Italy",
            "255" => "Portugal", "256" => "Malta", "257" => "Norway",
            "258" => "Norway", "259" => "Norway", "261" => "Poland",
            "263" => "Portugal", "265" or "266" or "267" => "Sweden",
            "269" => "Switzerland", "270" => "Czech Republic", "271" => "Turkey",
            "272" => "Ukraine", "273" => "Russia", "274" => "Russia",
            "275" => "Latvia", "276" => "Estonia", "277" => "Lithuania",
            "278" => "Slovenia", "279" => "Croatia",
            "301" => "Anguilla", "303" => "Alaska", "304" or "305" => "Antigua",
            "309" => "Bahamas", "310" => "Bermuda", "311" => "Bahamas",
            "312" => "Belize", "314" => "Barbados",
            "316" => "Canada", "319" => "Cayman Islands",
            "325" => "Christmas Island", "327" => "Cook Islands",
            "338" or "366" or "367" or "368" or "369" => "United States",
            "370" or "371" or "372" or "373" or "374" or "375" or "376" or "377" => "Panama",
            "378" => "Bosnia", "379" => "US Virgin Islands",
            "401" => "Afghanistan", "403" => "Saudi Arabia",
            "412" => "China", "413" => "China", "414" => "China",
            "416" => "Taiwan", "417" => "Sri Lanka",
            "419" => "India", "422" => "Iran", "423" => "Azerbaijan",
            "428" => "Israel", "431" => "Japan", "432" => "Japan",
            "440" => "South Korea", "441" => "South Korea",
            "443" => "Palestine", "445" => "North Korea",
            "447" => "Kuwait", "450" => "Lebanon",
            "455" => "Maldives", "457" => "Mongolia",
            "459" => "Nepal", "461" => "Oman", "463" => "Pakistan",
            "466" or "467" => "Qatar", "468" => "Syria",
            "470" => "UAE", "471" => "UAE", "472" => "Tajikistan",
            "473" => "Yemen", "475" => "Saudi Arabia",
            "477" => "Hong Kong",
            "501" => "Antarctica", "503" => "Australia",
            "510" => "New Zealand",
            "511" => "Palau", "512" => "Fiji", "514" => "Micronesia",
            "515" => "Myanmar", "516" => "Vanuatu",
            "518" => "New Caledonia",
            "520" => "Papua New Guinea", "523" => "Australia",
            "525" => "Indonesia", "529" => "Kiribati",
            "531" => "Laos", "533" => "Malaysia",
            "536" => "Marshall Islands", "538" => "Marshall Islands",
            "540" => "New Caledonia", "542" => "Niue",
            "544" => "Nauru", "546" => "French Polynesia",
            "548" => "Philippines", "553" => "Papua New Guinea",
            "555" => "Solomon Islands", "557" => "Tuvalu",
            "559" => "American Samoa", "561" => "Wallis",
            "563" => "Singapore", "564" => "Singapore",
            "565" => "Singapore", "566" => "Singapore",
            "567" => "Thailand", "570" => "Tonga",
            "572" => "Tuvalu", "574" => "Vietnam",
            "576" => "Tuvalu", "577" => "Vanuatu",
            "578" => "Wallis",
            "601" => "South Africa", "603" => "Angola",
            "605" => "Algeria", "607" => "France (St Paul)",
            "608" => "UK (Ascension)", "609" => "Burundi",
            "610" => "Benin", "611" => "Botswana",
            "612" => "Central African Republic",
            "613" => "Cameroon", "615" => "Congo",
            "616" => "Comoros", "617" => "Cape Verde",
            "618" => "Djibouti",
            "619" => "Egypt", "620" => "Egypt",
            "621" => "Equatorial Guinea", "622" => "Ethiopia",
            "624" => "Eritrea", "625" => "Gabon",
            "626" => "Ghana", "627" => "Gambia",
            "629" => "Guinea-Bissau", "630" => "Guinea",
            "631" => "Burkina Faso", "632" => "Kenya",
            "633" => "Comoros", "634" => "Liberia",
            "635" => "Liberia", "636" => "Liberia",
            "637" => "Liberia", "638" => "South Sudan",
            "642" => "Libya", "644" => "Lesotho",
            "645" => "Mauritius", "647" => "Madagascar",
            "649" => "Mali", "650" => "Mozambique",
            "654" => "Mauritania", "655" => "Malawi",
            "656" => "Niger", "657" => "Nigeria",
            "659" => "Namibia", "660" => "Reunion",
            "661" => "Rwanda", "662" => "Sudan",
            "663" => "Senegal", "664" => "Seychelles",
            "665" => "UK (St Helena)", "666" => "Somalia",
            "667" => "Sierra Leone", "668" => "Sao Tome",
            "669" => "Eswatini", "670" => "Chad",
            "671" => "Togo", "672" => "Tunisia",
            "674" => "Tanzania", "675" => "Uganda",
            "676" => "DR Congo", "677" => "Tanzania",
            "678" => "Zambia", "679" => "Zimbabwe",
            _ => null,
        };
    }
}
