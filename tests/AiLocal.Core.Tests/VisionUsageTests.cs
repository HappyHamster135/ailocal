using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>v1.91: visionsanropens usage extraheras ur svaren så de kan
/// PRISSÄTTAS i uppdragets kostnadsredovisning. Låser alla fyra leverantörs-
/// parsrarna: tokens + modellslug (normaliserad till OpenRouter-form där
/// prislistan kräver det) och att usage-lösa svar ger 0/null (räknas då
/// oprissatt av fotnoten i stället - aldrig en gissad siffra).</summary>
public class VisionUsageTests
{
    [Fact]
    public void OpenAiDirekt_PrefixasForPrislistan()
    {
        var r = VisionAnalyzer.ParseOpenAIVisionResponse("""
            {"model":"gpt-4o-2024-08-06",
             "choices":[{"message":{"content":"Spelet ser korrekt ut."}}],
             "usage":{"prompt_tokens":1200,"completion_tokens":80}}
            """, providerName: "openai");
        Assert.True(r.Success);
        Assert.Equal("openai/gpt-4o-2024-08-06", r.Model);   // prislistan är slug-nycklad
        Assert.Equal(1200, r.InputTokens);
        Assert.Equal(80, r.OutputTokens);
    }

    [Fact]
    public void OpenRouter_BeharSlugSomDenAr()
    {
        var r = VisionAnalyzer.ParseOpenAIVisionResponse("""
            {"model":"google/gemini-2.5-flash",
             "choices":[{"message":{"content":"Titelskärmen har namn och startval."}}],
             "usage":{"prompt_tokens":900,"completion_tokens":55}}
            """, providerName: "openrouter");
        Assert.Equal("google/gemini-2.5-flash", r.Model);    // redan sluggad - prefixas inte
        Assert.Equal(900, r.InputTokens);
    }

    [Fact]
    public void Anthropic_RaSlugOchUsage()
    {
        var r = VisionAnalyzer.ParseAnthropicVisionResponse("""
            {"model":"claude-3-5-sonnet-20241022",
             "content":[{"type":"text","text":"Spelplanen renderas."}],
             "usage":{"input_tokens":1500,"output_tokens":60}}
            """);
        Assert.True(r.Success);
        Assert.Equal("claude-3-5-sonnet-20241022", r.Model); // Anthropic-listan prissätter rå slug
        Assert.Equal(1500, r.InputTokens);
        Assert.Equal(60, r.OutputTokens);
    }

    [Fact]
    public void Gemini_UsageMetadataOchGooglePrefix()
    {
        var r = VisionAnalyzer.ParseGeminiVisionResponse("""
            {"candidates":[{"content":{"parts":[{"text":"Allt ser bra ut."}]}}],
             "modelVersion":"gemini-2.5-flash",
             "usageMetadata":{"promptTokenCount":1100,"candidatesTokenCount":40}}
            """);
        Assert.True(r.Success);
        Assert.Equal("google/gemini-2.5-flash", r.Model);
        Assert.Equal(1100, r.InputTokens);
        Assert.Equal(40, r.OutputTokens);
    }

    [Fact]
    public void Gemini_UtanModelVersion_FallerTillbakaPaRequestModellen()
    {
        var r = VisionAnalyzer.ParseGeminiVisionResponse("""
            {"candidates":[{"content":{"parts":[{"text":"ok"}]}}]}
            """, requestModel: "gemini-2.0-flash");
        Assert.Equal("google/gemini-2.0-flash", r.Model);
        Assert.Equal(0, r.InputTokens);   // ingen usage -> 0 -> räknas oprissatt
    }

    [Fact]
    public void SvarUtanUsage_GerNollTokens_SaFotnotenTarVid()
    {
        var r = VisionAnalyzer.ParseOpenAIVisionResponse("""
            {"choices":[{"message":{"content":"utan usage-fält"}}]}
            """, providerName: "openai");
        Assert.True(r.Success);
        Assert.Null(r.Model);
        Assert.Equal(0, r.InputTokens);
        Assert.Equal(0, r.OutputTokens);
    }
}
