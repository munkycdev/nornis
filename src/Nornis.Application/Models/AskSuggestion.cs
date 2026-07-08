namespace Nornis.Application.Models;

/// <summary>
/// A suggested question for the Ask hero / empty state, generated from live campaign data.
/// Category is one of: storyline, character, rumor, world, recap.
/// </summary>
public record AskSuggestion(string Text, string Category);
