namespace IpsecStudio;

// Packets seen on a responder's network adapter (NDIS capture), bucketed on the host by source, destination,
// destination port (0 for ESP) and kind ("esp" or "tcp" in clear). Only counts leave the host; the capture is deleted there.
public sealed record WireBucket(string Source, string Destination, int Port, string Kind, int Count);
public sealed record CaptureHost(string Host, bool Ok, string? Error, long Bytes, string? Sha256, WireBucket[] Buckets);
// What the wire showed for one probe pair. Match is null when there is nothing to judge (indeterminate/unreachable).
public sealed record WireObservation(int Esp, int Clear, bool? Match);

public static class Wire {
    // ESP counts in both directions between the pair (the reply travels protected too); clear TCP only initiator -> responder:port.
    public static (int Esp, int Clear) Count(IEnumerable<WireBucket> buckets, string initiatorIp, string responderIp, int port) {
        var esp = 0; var clear = 0;
        foreach (var b in buckets) {
            var pair = (b.Source == initiatorIp && b.Destination == responderIp) || (b.Source == responderIp && b.Destination == initiatorIp);
            if (!pair) continue;
            if (b.Kind == "esp") esp += b.Count;
            else if (b.Kind == "tcp" && b.Source == initiatorIp && b.Port == port) clear += b.Count;
        }
        return (esp, clear);
    }
    // ESP proves protection but not whether it is encrypted or integrity-only: that distinction stays with the SA.
    public static bool? Judge(string expected, int esp, int clear) => expected switch {
        "encrypted" or "authenticated" => esp > 0 && clear == 0,
        "clear" => clear > 0 && esp == 0,
        "blocked" => esp == 0,
        _ => null,
    };
}
