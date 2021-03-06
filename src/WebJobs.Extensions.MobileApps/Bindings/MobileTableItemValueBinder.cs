﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.WindowsAzure.MobileServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Extensions.MobileApps
{
    internal class MobileTableItemValueBinder<T> : IValueBinder
    {
        private MobileTableContext _context;
        private JObject _originalItem;

        public MobileTableItemValueBinder(MobileTableContext context)
        {
            _context = context;
        }

        public Type Type
        {
            get { return typeof(T); }
        }

        public async Task SetValueAsync(object value, CancellationToken cancellationToken)
        {
            if (value == null || _originalItem == null)
            {
                return;
            }

            await SetValueInternalAsync(_originalItem, value, _context);
        }

        public object GetValue()
        {
            object item = null;

            if (typeof(T) == typeof(JObject))
            {
                IMobileServiceTable table = _context.Client.GetTable(_context.ResolvedAttribute.TableName);
                IgnoreNotFoundException(() =>
                {
                    item = table.LookupAsync(_context.ResolvedAttribute.Id).Result;
                    _originalItem = CloneItem(item);
                });
            }
            else
            {
                // If TableName is specified, add it to the internal table cache. Now items of this type
                // will operate on the specified TableName.
                if (!string.IsNullOrEmpty(_context.ResolvedAttribute.TableName))
                {
                    _context.Client.AddToTableNameCache(typeof(T), _context.ResolvedAttribute.TableName);
                }

                IMobileServiceTable<T> table = _context.Client.GetTable<T>();
                IgnoreNotFoundException(() =>
                {
                    item = table.LookupAsync(_context.ResolvedAttribute.Id).Result;
                    _originalItem = CloneItem(item);
                });
            }

            return item;
        }

        public string ToInvokeString()
        {
            return string.Empty;
        }

        internal static async Task SetValueInternalAsync(JObject originalItem, object newItem, MobileTableContext context)
        {
            JObject currentValue = null;
            bool isJObject = newItem.GetType() == typeof(JObject);

            if (isJObject)
            {
                currentValue = newItem as JObject;
            }
            else
            {
                currentValue = JObject.FromObject(newItem);
            }

            if (HasChanged(originalItem, currentValue))
            {
                // make sure it's not the Id that has changed
                if (!string.Equals(GetId(originalItem), GetId(currentValue), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Cannot update the 'Id' property.");
                }

                if (isJObject)
                {
                    IMobileServiceTable table = context.Client.GetTable(context.ResolvedAttribute.TableName);
                    await table.UpdateAsync((JObject)newItem);
                }
                else
                {
                    // If TableName is specified, add it to the internal table cache. Now items of this type
                    // will operate on the specified TableName.
                    if (!string.IsNullOrEmpty(context.ResolvedAttribute.TableName))
                    {
                        context.Client.AddToTableNameCache(newItem.GetType(), context.ResolvedAttribute.TableName);
                    }
                    IMobileServiceTable<T> table = context.Client.GetTable<T>();
                    await table.UpdateAsync((T)newItem);
                }
            }
        }

        internal static string GetId(JObject item)
        {
            JToken idToken = item.GetValue("id", StringComparison.OrdinalIgnoreCase);
            return idToken.ToString();
        }

        internal static bool HasChanged(JToken original, JToken current)
        {
            return !JToken.DeepEquals(original, current);
        }

        internal static JObject CloneItem(object item)
        {
            string serializedItem = JsonConvert.SerializeObject(item);
            return JObject.Parse(serializedItem);
        }

        private static void IgnoreNotFoundException(Action action)
        {
            try
            {
                action();
            }
            catch (AggregateException ex)
            {
                foreach (Exception innerEx in ex.InnerExceptions)
                {
                    MobileServiceInvalidOperationException mobileEx =
                        innerEx as MobileServiceInvalidOperationException;
                    if (mobileEx == null ||
                        mobileEx.Response.StatusCode != System.Net.HttpStatusCode.NotFound)
                    {
                        throw innerEx;
                    }
                }
            }
        }
    }
}