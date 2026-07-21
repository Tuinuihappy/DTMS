namespace DTMS.Facility.Domain.Services;

// Normalization rules for matching user-supplied station-code input.
// Two deliberately different shapes:
//  • NormalizeExact — for resolution (code → single station): TRIM+UPPER,
//    the stored form enforced by Station.SetCode. Never strips separators —
//    "STF_2" must not resolve to "STF_29".
//  • NormalizeQuery — for substring search: TRIM+UPPER plus separator
//    stripping so "stf-02" / "STF 02" find "STF_02". Mirrors the frontend
//    picker normalizer (frontend/lib/utils.ts normalizeSearchText). A
//    separator-only input normalizes to "" — callers treat that as
//    "no filter".
public static class StationCodeSearch
{
    public static string NormalizeExact(string code) => code.Trim().ToUpperInvariant();

    public static string NormalizeQuery(string query) =>
        NormalizeExact(query).Replace("_", "").Replace("-", "").Replace(" ", "");
}
