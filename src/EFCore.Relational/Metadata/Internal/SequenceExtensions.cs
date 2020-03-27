// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Text;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Microsoft.EntityFrameworkCore.Metadata.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public static class SequenceExtensions
    {
        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public static string ToDebugString(
            [NotNull] this ISequence sequence,
            MetadataDebugStringOptions options,
            [NotNull] string indent = "")
        {
            var builder = new StringBuilder();

            builder
                .Append(indent)
                .Append("Sequence: ");

            if (sequence.Schema != null)
            {
                builder
                    .Append(sequence.Schema)
                    .Append(".");
            }

            builder.Append(sequence.Name);

            if (!sequence.IsCyclic)
            {
                builder.Append(" Cyclic");
            }

            if (sequence.StartValue != 1)
            {
                builder.Append(" Start: ")
                    .Append(sequence.StartValue);
            }

            if (sequence.IncrementBy != 1)
            {
                builder.Append(" IncrementBy: ")
                    .Append(sequence.IncrementBy);
            }

            if (sequence.MinValue != null)
            {
                builder.Append(" Min: ")
                    .Append(sequence.MinValue);
            }

            if (sequence.MaxValue != null)
            {
                builder.Append(" Max: ")
                    .Append(sequence.MaxValue);
            }

            if ((options & MetadataDebugStringOptions.SingleLine) == 0)
            {
                if ((options & MetadataDebugStringOptions.IncludeAnnotations) != 0)
                {
                    builder.Append(sequence.AnnotationsToDebugString(indent: indent + "  "));
                }
            }

            return builder.ToString();
        }
    }
}
