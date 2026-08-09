using System.Globalization;
using System.Text;

using Cynara.Domain.Patients;

namespace Cynara.Application.Modules.Patients;

/// <summary>
/// Shared validation and normalization helpers for the patient lifecycle.
/// Keeps the patient service free of repeated string sanitation while
/// preserving the rules described in CYN-49: hospital-scoped MRN
/// uniqueness, optimistic concurrency, and rejection of demographic edits
/// against soft-deleted records.
/// </summary>
internal static class PatientWorkflowHelpers
{
    /// <summary>Maximum length for the displayed MRN.</summary>
    public const int MrnMaxLength = PatientFieldLimits.MrnMaxLength;

    /// <summary>Maximum length for the national identifier.</summary>
    public const int NationalIdMaxLength = PatientFieldLimits.NationalIdMaxLength;

    /// <summary>Maximum length for given and family names.</summary>
    public const int NameMaxLength = PatientFieldLimits.NameMaxLength;

    /// <summary>
    /// Returns the trimmed, upper-invariant comparison form used by the
    /// composite unique index and search comparisons.
    /// </summary>
    public static string NormalizeMrn(string mrn)
    {
        ArgumentException.ThrowIfNullOrEmpty(mrn);
        return mrn.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Returns the trimmed, upper-invariant comparison form for an
    /// optional MRN filter, or <see langword="null"/> when the input is
    /// null/whitespace.
    /// </summary>
    public static string? NormalizeMrnOrNull(string? mrn)
    {
        if (string.IsNullOrWhiteSpace(mrn))
        {
            return null;
        }

        return mrn.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Returns the trimmed, upper-invariant comparison form for the
    /// national identifier, or <see langword="null"/> when the input is
    /// null/whitespace.
    /// </summary>
    public static string? NormalizeNationalId(string? nationalId)
    {
        if (string.IsNullOrWhiteSpace(nationalId))
        {
            return null;
        }

        return nationalId.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Returns the trimmed, upper-invariant, diacritic-folded form used for
    /// name indexing and search so <c>rodri</c> matches <c>Rodríguez</c>.
    /// </summary>
    public static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        string trimmed = name.Trim().ToUpperInvariant();
        string formD = trimmed.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(formD.Length);
        foreach (char character in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                != UnicodeCategory.NonSpacingMark)
            {
                _ = builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Splits given/family name filters into search tokens. Tokens from
    /// either field must each appear somewhere in the patient's full
    /// normalized name (given + family), so <c>jorge rodri</c> matches
    /// <c>Jorge Soto Rodríguez</c>.
    /// </summary>
    public static IReadOnlyList<string> TokenizeNameFilter(
        string? givenName,
        string? familyName)
    {
        List<string> tokens = [];
        foreach (string? value in new[] { givenName, familyName })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (string part in value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string token = NormalizeName(part);
                if (token.Length > 0)
                {
                    tokens.Add(token);
                }
            }
        }

        return tokens;
    }

    /// <summary>
    /// Ensures the supplied MRN is non-empty and respects the configured
    /// length bounds.
    /// </summary>
    /// <exception cref="ValidationException">
    /// Thrown when the MRN is whitespace, or longer than the configured
    /// maximum length.
    /// </exception>
    public static void EnsureValidMrn(string mrn)
    {
        if (string.IsNullOrWhiteSpace(mrn))
        {
            throw new ValidationException("Patient MRN is required.");
        }

        string trimmed = mrn.Trim();
        if (trimmed.Length > MrnMaxLength)
        {
            throw new ValidationException(
                "Patient MRN must be "
                + MrnMaxLength.ToString(CultureInfo.InvariantCulture)
                + " characters or fewer.");
        }
    }

    /// <summary>
    /// Ensures the supplied national identifier respects the configured
    /// length bounds when provided.
    /// </summary>
    /// <exception cref="ValidationException">
    /// Thrown when the national identifier is longer than the configured
    /// maximum length.
    /// </exception>
    public static void EnsureValidNationalId(string? nationalId)
    {
        if (nationalId is null)
        {
            return;
        }

        if (nationalId.Length > NationalIdMaxLength)
        {
            throw new ValidationException(
                "Patient national identifier must be "
                + NationalIdMaxLength.ToString(CultureInfo.InvariantCulture)
                + " characters or fewer.");
        }
    }

    /// <summary>
    /// Ensures a name is present and respects the configured bounds.
    /// </summary>
    /// <exception cref="ValidationException">
    /// Thrown when the name is whitespace, or longer than the configured
    /// maximum length.
    /// </exception>
    public static void EnsureValidName(string name, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException(
                "Patient " + fieldName + " is required.");
        }

        if (name.Trim().Length > NameMaxLength)
        {
            throw new ValidationException(
                "Patient " + fieldName + " must be "
                + NameMaxLength.ToString(CultureInfo.InvariantCulture)
                + " characters or fewer.");
        }
    }

    /// <summary>
    /// Ensures the supplied date of birth is in a sensible range.
    /// </summary>
    /// <exception cref="ValidationException">
    /// Thrown when the birth date is in the future or older than the
    /// supported minimum age.
    /// </exception>
    public static void EnsureValidBirthDate(DateOnly birthDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        if (birthDate > today)
        {
            throw new ValidationException("Patient birth date cannot be in the future.");
        }

        if (birthDate < today.AddYears(-130))
        {
            throw new ValidationException("Patient birth date is unrealistically old.");
        }
    }

    /// <summary>
    /// Parses the supplied sex string into the <see cref="Sex"/> enum and
    /// throws <see cref="ValidationException"/> when the value is unknown.
    /// </summary>
    /// <exception cref="ValidationException">
    /// Thrown when the supplied sex value does not match a defined
    /// <see cref="Sex"/> member.
    /// </exception>
    public static Sex ParseSex(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (!Enum.TryParse(value, ignoreCase: true, out Sex parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new ValidationException(
                "Patient sex '" + value
                + "' is not one of: female, male, unknown.");
        }

        return parsed;
    }

    /// <summary>
    /// Canonical lowercase clinical notation for a blood type
    /// (<c>a+</c>, <c>ab-</c>, <c>o+</c>, …).
    /// </summary>
    public static string FormatBloodType(BloodType bloodType)
    {
        return bloodType switch
        {
            BloodType.APositive => "a+",
            BloodType.ANegative => "a-",
            BloodType.BPositive => "b+",
            BloodType.BNegative => "b-",
            BloodType.ABPositive => "ab+",
            BloodType.ABNegative => "ab-",
            BloodType.OPositive => "o+",
            BloodType.ONegative => "o-",
            _ => throw new ValidationException(
                $"Unknown patient blood type '{bloodType}'."),
        };
    }

    /// <summary>
    /// Parses the supplied clinical notation into the <see cref="BloodType"/>
    /// enum and throws <see cref="ValidationException"/> when the value is
    /// unknown.
    /// </summary>
    /// <exception cref="ValidationException">
    /// Thrown when the supplied blood type does not match a defined
    /// <see cref="BloodType"/> member.
    /// </exception>
    public static BloodType ParseBloodType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException("Patient blood type is required.");
        }

        string normalized = value.Trim().ToLowerInvariant();
        BloodType parsed = normalized switch
        {
            "a+" => BloodType.APositive,
            "a-" => BloodType.ANegative,
            "b+" => BloodType.BPositive,
            "b-" => BloodType.BNegative,
            "ab+" => BloodType.ABPositive,
            "ab-" => BloodType.ABNegative,
            "o+" => BloodType.OPositive,
            "o-" => BloodType.ONegative,
            _ => throw new ValidationException(
                "Patient blood type '" + value
                + "' is not one of: a+, a-, b+, b-, ab+, ab-, o+, o-."),
        };

        return parsed;
    }

    /// <summary>
    /// Optimistic concurrency guard for patient updates.
    /// </summary>
    /// <exception cref="ConcurrencyException">
    /// Thrown when the stored row version does not match the version
    /// supplied by the caller.
    /// </exception>
    public static void EnsureConcurrency(uint current, uint provided)
    {
        if (current != provided)
        {
            throw new ConcurrencyException(
                "The patient was modified by another request.");
        }
    }

    /// <summary>
    /// Ensures the patient has not been soft-deleted.
    /// </summary>
    /// <exception cref="InvalidStateException">
    /// Thrown when the patient has been soft-deleted and may no longer be
    /// mutated.
    /// </exception>
    public static void EnsureNotDeleted(Patient patient)
    {
        if (patient.DeletedAt is not null)
        {
            throw new InvalidStateException(
                "Patient '" + patient.Id
                + "' is deleted and cannot be modified.");
        }
    }
}
