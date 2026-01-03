//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using System;
using System.Text.Json.Serialization;

namespace Jaya.Shared.Base
{
    public abstract class ConfigModelBase : ModelBase
    {
        // Note: System.Text.Json does not provide a class-level OptIn attribute equivalent to
        // Json.NET's [JsonObject(MemberSerialization.OptIn)]. To achieve opt-in semantics,
        // mark individual members with [JsonInclude] or make them public and configure
        // JsonSerializerOptions appropriately where serialization is performed.
        internal static T Empty<T>() where T : ConfigModelBase
        {
            return (T)Activator.CreateInstance<T>().Empty();
        }

        protected abstract ConfigModelBase Empty();
    }
}
