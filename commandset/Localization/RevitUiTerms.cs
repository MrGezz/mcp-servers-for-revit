namespace RevitMCPCommandSet.Localization
{
    /// <summary>
    /// Values that appear in a LOCALIZED Revit user interface, which these tools have
    /// to recognise in order to work outside an English install.
    ///
    /// This is a different thing from Strings.T. Those are messages this project
    /// AUTHORS and may translate for the reader. These are strings REVIT produces -
    /// parameter names, family type names, the words a boolean parameter takes - and
    /// the code has to match them to find anything.
    ///
    /// They used to be Chinese literals scattered through control flow:
    ///
    ///     case "..."                                  in CreateScheduleEventHandler
    ///     if (paramName.Contains("..."))              in ColorSplashEventHandler
    ///     _floorName = "... - "                       in CreateSurfaceElementEventHandler
    ///     def.Name == "..." || def.Name == "Name"     in RenameElementEventHandler
    ///
    /// Deleting them to make the source English-only would have quietly broken every
    /// one of those tools on a Chinese Revit; translating them in place would have
    /// done the same. So the source keeps ENGLISH defaults, and the localized aliases
    /// live as DATA in the locale catalogue (Localization/zh-Hans.json, "terms"),
    /// where they are visible, documented and extensible to other languages.
    ///
    /// Aliases are always ADDITIVE: the English forms are matched no matter which
    /// locale is configured, so behaviour never gets worse than English-only.
    /// </summary>
    public static class RevitUiTerms
    {
        /// <summary>Schedule kinds accepted by create_schedule.</summary>
        public const string ScheduleGeneral = "general";
        public const string ScheduleMaterial = "material";
        public const string ScheduleKey = "key";
        public const string ScheduleViewList = "view list";
        public const string ScheduleSheetList = "sheet list";
        public const string ScheduleRevision = "revision";

        /// <summary>The "Generic" family-type prefix Revit uses for default types.</summary>
        public const string GenericTypePrefix = "Generic";

        /// <summary>The "Name" parameter, as a family/type definition calls it.</summary>
        public const string NameParameter = "Name";

        /// <summary>Three-dimensional view, as create_view names it.</summary>
        public const string ThreeD = "3d";

        /// <summary>
        /// True when <paramref name="candidate"/> matches <paramref name="englishTerm"/>
        /// in English or in any alias the active locale catalogue supplies.
        /// Comparison is case-insensitive and ignores surrounding whitespace.
        /// </summary>
        public static bool Matches(string englishTerm, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            string probe = candidate.Trim();

            if (string.Equals(probe, englishTerm, StringComparison.OrdinalIgnoreCase)) return true;

            foreach (string alias in Strings.TermAliases(englishTerm))
            {
                if (string.Equals(probe, alias, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>
        /// True when <paramref name="candidate"/> CONTAINS the English term or any of its
        /// localized aliases. For matching Revit type names such as "Generic - 200mm",
        /// where the term is a prefix rather than the whole value.
        /// </summary>
        public static bool Contains(string englishTerm, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;

            if (candidate.IndexOf(englishTerm, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            foreach (string alias in Strings.TermAliases(englishTerm))
            {
                if (candidate.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        /// <summary>
        /// True when the value reads as an affirmative in English or in the active
        /// locale. Used to recognise yes/no parameters, whose displayed values are
        /// localized by Revit.
        /// </summary>
        public static bool IsAffirmative(string value) => Matches("yes", value) || Matches("true", value);

        /// <summary>True when the value reads as a negative.</summary>
        public static bool IsNegative(string value) => Matches("no", value) || Matches("false", value);

        /// <summary>
        /// True when a parameter NAME looks like a yes/no parameter. English installs
        /// use "Is"/"Has"; other locales use their own wording, supplied as aliases.
        /// </summary>
        public static bool LooksBoolean(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName)) return false;
            return Contains("Is ", parameterName)
                   || Contains("Has ", parameterName)
                   || Contains("boolean-parameter-prefix", parameterName);
        }
    }
}
