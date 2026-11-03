namespace Nornis.Application.Prep;

/// <summary>
/// The markup convention Prep writes in, and the one place it is defined. A plain blockquote
/// is read-aloud text. A blockquote whose first line is <see cref="PropMarker"/> is a prop the
/// GM hands over, one whose first line is <see cref="AskMarker"/> is a question for the
/// players, and one whose first line is <see cref="GmMarker"/> is a callout for the GM alone
/// (a hard exit, a chaos valve, a reminder: feature 35). A fenced block whose info string is <see cref="StatBlockInfo"/> is a stat block,
/// and one whose info string is <see cref="ItemBlockInfo"/> is a piece of equipment: free
/// "Label: value" lines with an optional name, subtitle and groups (<see cref="GameBlocks"/>),
/// system-agnostic, stored and displayed, never computed.
///
/// The import parser, the print renderer and the drafting prompt read these. The Web's
/// editor toolbar can't reference Application, so it keeps a mirror that
/// <c>PrepMarkupMirrorTests</c> pins to this file.
/// </summary>
public static class PrepMarkup
{
    public const string PropMarker = "[!PROP]";

    public const string AskMarker = "[!ASK]";

    public const string GmMarker = "[!GM]";

    public const string StatBlockInfo = "statblock";

    public const string ItemBlockInfo = "item";

    /// <summary>
    /// How the Loremaster writes for an adventure: the one statement of these conventions both
    /// Prep prompts carry (feature 35), so drafting and the conversation can't drift apart.
    /// </summary>
    public static readonly string ConventionsForPrompt = $$"""
        - Read-aloud text is a plain blockquote: lines starting with "> ".
        - A prop the players can hold or read is a blockquote whose first line is exactly
          "> {{PropMarker}}", followed by its text on the next "> " lines.
        - A question the GM puts to the players is a blockquote whose first line is exactly
          "> {{AskMarker}}", followed by the question.
        - A callout for the GM alone (a hard exit, a fail-forward, a reminder to watch the clock)
          is a blockquote whose first line is exactly "> {{GmMarker}}", followed by its text. Start
          the text with a short bold label, such as "**Hard exit.**".
        - A creature's or person's game statistics are a fenced code block with the info string
          "{{StatBlockInfo}}". A piece of equipment is one with the info string "{{ItemBlockInfo}}".
          Inside either:
          - the first line is "# " and the name, and the next may be a subtitle in single
            asterisks (kind, size, rarity: whatever the game system puts there);
          - then one "Label: value" pair per line;
          - in a stat block, a "## " line starts a group such as Traits or Actions;
          - in an item, a line of "---" ends the pairs, and the description follows it.
        - Game numbers are labels and values, nothing computed and no rules engine. Use the
          world's game system's usual labels if it has one, and plain ones otherwise.
        - When the world's record already has a "stat block" or "item details" fact for someone
          or something, use those numbers rather than inventing new ones.
        - Don't reproduce a published book's stat block. Name the book and the page instead, and
          give only what the scene needs.
        - Tables are Markdown pipe tables. Subheadings start at "###".

        For example:

        ```{{StatBlockInfo}}
        # Captain Voss
        *Medium human, pirate captain*
        Defense: 15
        Health: 27
        Speed: 30 ft., swim 20 ft.
        ## Actions
        Cutlass: +5 to hit, 1d8+3 slashing.
        ```

        ```{{ItemBlockInfo}}
        # Cloak of the Tidecaller
        *Wondrous item, rare*
        Weight: 1 lb
        ---
        While you wear it you can breathe water.
        ```
        """;
}
