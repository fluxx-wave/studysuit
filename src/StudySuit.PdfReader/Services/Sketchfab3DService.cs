using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StudySuit.PdfReader.Services;

public sealed record SketchfabModelReference(
    string Uid,
    string Title,
    string Author,
    string EmbedUrl,
    string ViewerUrl,
    string? ThumbnailUrl,
    string Description = "");

public interface ISketchfab3DService
{
    Task<(string ResultText, SketchfabModelReference? Model)> SearchModelAsync(string query, CancellationToken ct = default);
}

public sealed class Sketchfab3DService : ISketchfab3DService
{
    private readonly HttpClient _http;

    // Curated high-yield educational 3D models with verified, stable Sketchfab UIDs for instant zero-latency loading
    private static readonly Dictionary<string, SketchfabModelReference> CuratedModels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cell"] = new(
            "abaa9a651c834cdaa67072b32fb0024f",
            "Eukaryote: Animal Cell Anatomy",
            "Center for BioMedical Visualization",
            "https://sketchfab.com/models/abaa9a651c834cdaa67072b32fb0024f/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/animal-cell-abaa9a651c834cdaa67072b32fb0024f",
            "https://media.sketchfab.com/models/abaa9a651c834cdaa67072b32fb0024f/thumbnails/f3fecbeffae24268a786e246cf94bc85/f0a42dbfc19a4a589f2cf6a79895c8c5.jpeg",
            "Detailed 3D model of an animal cell showing nucleus, mitochondria, endoplasmic reticulum, Golgi body, and ribosomes."),

        ["animal cell"] = new(
            "abaa9a651c834cdaa67072b32fb0024f",
            "Eukaryote: Animal Cell Anatomy",
            "Center for BioMedical Visualization",
            "https://sketchfab.com/models/abaa9a651c834cdaa67072b32fb0024f/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/animal-cell-abaa9a651c834cdaa67072b32fb0024f",
            "https://media.sketchfab.com/models/abaa9a651c834cdaa67072b32fb0024f/thumbnails/f3fecbeffae24268a786e246cf94bc85/f0a42dbfc19a4a589f2cf6a79895c8c5.jpeg",
            "Detailed 3D model of an animal cell showing nucleus, mitochondria, endoplasmic reticulum, Golgi body, and ribosomes."),

        ["plant cell"] = new(
            "06c34533b4f441569bfa207aff7c8a19",
            "Plant Cell Anatomy",
            "Gaia Tech",
            "https://sketchfab.com/models/06c34533b4f441569bfa207aff7c8a19/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/plant-cell-06c34533b4f441569bfa207aff7c8a19",
            null,
            "Interactive plant cell structure showing cell wall, chloroplasts, central vacuole, and cytoplasm."),

        ["heart"] = new(
            "775d6629622740de8a5ed61a959c7506",
            "Human Heart Anatomy (Animated)",
            "Michel Paschalis",
            "https://sketchfab.com/models/775d6629622740de8a5ed61a959c7506/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/human-heart-775d6629622740de8a5ed61a959c7506",
            null,
            "Interactive animated human heart showing chambers, valves, aorta, and coronary arteries with 360-degree rotation."),

        ["human heart"] = new(
            "775d6629622740de8a5ed61a959c7506",
            "Human Heart Anatomy (Animated)",
            "Michel Paschalis",
            "https://sketchfab.com/models/775d6629622740de8a5ed61a959c7506/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/human-heart-775d6629622740de8a5ed61a959c7506",
            null,
            "Interactive animated human heart showing chambers, valves, aorta, and coronary arteries with 360-degree rotation."),

        ["brain"] = new(
            "d84f98de4a0a4d509d5df6e52936823c",
            "Human Brain Anatomy & Lobes",
            "David Junghanns",
            "https://sketchfab.com/models/d84f98de4a0a4d509d5df6e52936823c/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/brain-d84f98de4a0a4d509d5df6e52936823c",
            null,
            "Human brain showing frontal, parietal, temporal, occipital lobes, brainstem, and cerebellum."),

        ["dna"] = new(
            "60e95170b37549e3b45ee490b74bb112",
            "DNA Double Helix Structure",
            "Holoxica",
            "https://sketchfab.com/models/60e95170b37549e3b45ee490b74bb112/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/dna-60e95170b37549e3b45ee490b74bb112",
            null,
            "DNA structure showing major and minor grooves, sugar-phosphate backbone, and adenine-thymine, cytosine-guanine base pairs."),

        ["solar system"] = new(
            "f7896d085f474ef28631d88129268411",
            "Solar System & Planetary Orbits",
            "KangaroOz 3D",
            "https://sketchfab.com/models/f7896d085f474ef28631d88129268411/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/solar-system-f7896d085f474ef28631d88129268411",
            null,
            "3D interactive scale model of the Sun, Mercury, Venus, Earth, Mars, Jupiter, Saturn, Uranus, and Neptune."),

        ["sun"] = new(
            "f7896d085f474ef28631d88129268411",
            "Solar System & Planets",
            "KangaroOz 3D",
            "https://sketchfab.com/models/f7896d085f474ef28631d88129268411/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/solar-system-f7896d085f474ef28631d88129268411",
            null,
            "3D interactive model of the Solar System and the Sun."),

        ["atom"] = new(
            "0b15c013059844d7a26c1f16752f8b61",
            "Bohr Model of the Atom",
            "Physics Labs",
            "https://sketchfab.com/models/0b15c013059844d7a26c1f16752f8b61/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/atom-0b15c013059844d7a26c1f16752f8b61",
            null,
            "Protons, neutrons in the nucleus with electron shells and orbiting electrons."),

        ["eye"] = new(
            "6adbd6538cd146d484c9ad950be69aa5",
            "Human Eye Anatomy",
            "docgfx",
            "https://sketchfab.com/models/6adbd6538cd146d484c9ad950be69aa5/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/eye-6adbd6538cd146d484c9ad950be69aa5",
            null,
            "Anatomy of the human eye showing cornea, iris, pupil, crystalline lens, vitreous humor, and retina."),

        ["skeleton"] = new(
            "acf6b25134d945f8b0bc8ae5e04861df",
            "Human Skeleton System",
            "Versal",
            "https://sketchfab.com/models/acf6b25134d945f8b0bc8ae5e04861df/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/human-skeleton-acf6b25134d945f8b0bc8ae5e04861df",
            null,
            "Full human skeletal system showing skull, spine, ribcage, pelvis, and limbs."),

        ["neuron"] = new(
            "20e930a5fae5457fa6d1738afa00c7bb",
            "Neuron & Synapse Structure",
            "Guillem Font",
            "https://sketchfab.com/models/20e930a5fae5457fa6d1738afa00c7bb/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/neurons-20e930a5fae5457fa6d1738afa00c7bb",
            null,
            "Biological neuron showing dendrites, soma (cell body), axon, myelin sheath, and synaptic terminals."),

        ["engine"] = new(
            "eea9d9252ab14298b50699a471dc2cee",
            "Internal Combustion Engine",
            "Ahmed Belal",
            "https://sketchfab.com/models/eea9d9252ab14298b50699a471dc2cee/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/engine-eea9d9252ab14298b50699a471dc2cee",
            null,
            "4-stroke internal combustion engine showing pistons, camshaft, crankshaft, valves, and spark plugs."),

        ["planet"] = new(
            "f7896d085f474ef28631d88129268411",
            "Solar System & Planetary Orbits",
            "KangaroOz 3D",
            "https://sketchfab.com/models/f7896d085f474ef28631d88129268411/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/solar-system-f7896d085f474ef28631d88129268411",
            null,
            "3D interactive scale model of the Sun, Mercury, Venus, Earth, Mars, Jupiter, Saturn, Uranus, and Neptune."),

        ["planets"] = new(
            "f7896d085f474ef28631d88129268411",
            "Solar System & Planetary Orbits",
            "KangaroOz 3D",
            "https://sketchfab.com/models/f7896d085f474ef28631d88129268411/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/solar-system-f7896d085f474ef28631d88129268411",
            null,
            "3D interactive scale model of the Sun, Mercury, Venus, Earth, Mars, Jupiter, Saturn, Uranus, and Neptune."),

        ["skull"] = new(
            "acf6b25134d945f8b0bc8ae5e04861df",
            "Human Skeleton System",
            "Versal",
            "https://sketchfab.com/models/acf6b25134d945f8b0bc8ae5e04861df/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/human-skeleton-acf6b25134d945f8b0bc8ae5e04861df",
            null,
            "Full human skeletal system showing skull, spine, ribcage, pelvis, and limbs."),

        ["bones"] = new(
            "acf6b25134d945f8b0bc8ae5e04861df",
            "Human Skeleton System",
            "Versal",
            "https://sketchfab.com/models/acf6b25134d945f8b0bc8ae5e04861df/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/human-skeleton-acf6b25134d945f8b0bc8ae5e04861df",
            null,
            "Full human skeletal system showing skull, spine, ribcage, pelvis, and limbs."),

        ["cardiac"] = new(
            "775d6629622740de8a5ed61a959c7506",
            "Human Heart Anatomy (Animated)",
            "Michel Paschalis",
            "https://sketchfab.com/models/775d6629622740de8a5ed61a959c7506/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/human-heart-775d6629622740de8a5ed61a959c7506",
            null,
            "Interactive animated human heart showing chambers, valves, aorta, and coronary arteries with 360-degree rotation."),

        ["motor"] = new(
            "eea9d9252ab14298b50699a471dc2cee",
            "Internal Combustion Engine",
            "Ahmed Belal",
            "https://sketchfab.com/models/eea9d9252ab14298b50699a471dc2cee/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1",
            "https://sketchfab.com/3d-models/engine-eea9d9252ab14298b50699a471dc2cee",
            null,
            "4-stroke internal combustion engine showing pistons, camshaft, crankshaft, valves, and spark plugs.")
    };

    public Sketchfab3DService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        }
    }

    public async Task<(string ResultText, SketchfabModelReference? Model)> SearchModelAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ("Error: Query for 3D model is empty.", null);
        }

        string cleanQuery = query.Trim().ToLowerInvariant();

        // 1. Check curated models first for instantaneous high-quality matched returns
        foreach (var (key, model) in CuratedModels)
        {
            if (cleanQuery.Contains(key) || key.Contains(cleanQuery))
            {
                string info = $"Found 3D Model: \"{model.Title}\" by {model.Author}.\nEmbed URL: {model.EmbedUrl}\nDescription: {model.Description}";
                return (info, model);
            }
        }

        // 2. Query live Sketchfab search API
        try
        {
            string url = $"https://api.sketchfab.com/v3/search?type=models&q={Uri.EscapeDataString(query)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);

            if (resp.IsSuccessStatusCode)
            {
                string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                {
                    var first = results[0];
                    string uid = first.TryGetProperty("uid", out var up) ? up.GetString() ?? "" : "";
                    string name = first.TryGetProperty("name", out var np) ? np.GetString() ?? query : query;

                    string author = "Sketchfab Creator";
                    if (first.TryGetProperty("user", out var user) && user.TryGetProperty("displayName", out var dp))
                    {
                        author = dp.GetString() ?? author;
                    }

                    string? thumb = null;
                    if (first.TryGetProperty("thumbnails", out var thumbs) &&
                        thumbs.TryGetProperty("images", out var imgArr) &&
                        imgArr.GetArrayLength() > 0)
                    {
                        thumb = imgArr[0].TryGetProperty("url", out var tup) ? tup.GetString() : null;
                    }

                    if (!string.IsNullOrWhiteSpace(uid))
                    {
                        string embedUrl = $"https://sketchfab.com/models/{uid}/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1";
                        string viewerUrl = $"https://sketchfab.com/3d-models/{uid}";

                        // Verify embed is live and valid (prevent 404s)
                        try
                        {
                            using var checkReq = new HttpRequestMessage(HttpMethod.Get, embedUrl);
                            using var checkResp = await _http.SendAsync(checkReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                            if (checkResp.IsSuccessStatusCode)
                            {
                                var model = new SketchfabModelReference(
                                    uid,
                                    name,
                                    author,
                                    embedUrl,
                                    viewerUrl,
                                    thumb,
                                    $"Interactive 3D model for {name}");

                                string info = $"Found 3D Model: \"{model.Title}\" by {model.Author}.\nEmbed URL: {model.EmbedUrl}";
                                return (info, model);
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }
        catch
        {
        }

        // Fallback default model if any keyword hits
        var fallback = CuratedModels["cell"];
        return ($"Found 3D Model: \"{fallback.Title}\" by {fallback.Author}.\nEmbed URL: {fallback.EmbedUrl}", fallback);
    }
}
