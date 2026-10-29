namespace IpsecStudio;

// Packets seen on a responder's network adapter (NDIS capture), bucketed on the host by source, destination, port and kind:
// "esp"; "syn" (clear connection attempt, destination port); "synack" (clear acceptance, source port); "tcp" (other clear segment,
// destination port). Only counts leave the host; the capture is deleted there.
public sealed record WireBucket(string Source, string Destination, int Port, string Kind, int Count);
public sealed record CaptureHost(string Host, bool Ok, string? Error, long Bytes, string? Sha256, WireBucket[] Buckets);
// Clear = the responder accepted the connection in clear (SYN-ACK or later clear segments). Attempts = clear SYNs from the initiator:
// with outbound "Request" Windows sends the first SYN in clear while negotiating; a "Require" responder drops it.
// Match is null when there is nothing to judge (indeterminate/unreachable).
public sealed record WireObservation(int Esp, int Clear, int Attempts, bool? Match);

public static class Wire {
    // ESP is counted per pair of addresses (both directions, any connection between them): the reverse probe also lands here.
    public static (int Esp, int Clear, int Attempts) Count(IEnumerable<WireBucket> buckets, string initiatorIp, string responderIp, int port) {
        int esp = 0, clear = 0, attempts = 0;
        foreach (var b in buckets) {
            var forward = b.Source == initiatorIp && b.Destination == responderIp;
            var back = b.Source == responderIp && b.Destination == initiatorIp;
            if (!forward && !back) continue;
            if (b.Kind == "esp") esp += b.Count;
            else if (b.Port != port) continue;
            else if (b.Kind == "syn" && forward) attempts += b.Count;
            else if ((b.Kind == "synack" && back) || (b.Kind == "tcp" && forward)) clear += b.Count;
        }
        return (esp, clear, attempts);
    }
    // ESP proves protection but not whether it is encrypted or integrity-only: that distinction stays with the SA.
    public static bool? Judge(string expected, int esp, int clear) => expected switch {
        "encrypted" or "authenticated" => esp > 0 && clear == 0,
        "clear" => clear > 0,
        "blocked" => clear == 0,
        _ => null,
    };
}
