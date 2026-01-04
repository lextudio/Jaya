//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using System;
using System.Collections;
using System.Collections.Generic;

namespace Jaya.Shared
{
    /// <summary>
    /// Taken from https://www.c-sharpcorner.com/UploadFile/pranayamr/publisher-or-subscriber-pattern-with-event-or-delegate-and-e/
    /// </summary>
    public sealed class EventAggregator
    {
        readonly Dictionary<Type, IList> _subscribers;

        public EventAggregator()
        {
            _subscribers = new Dictionary<Type, IList>();
        }

        public void Publish<TMessageType>(TMessageType message)
        {
            Type type = typeof(TMessageType);
            if (_subscribers.TryGetValue(type, out var list))
            {
                foreach (var obj in list)
                {
                    if (obj is Subscription<TMessageType> action)
                        action.Action(message);
                }
            }
        }

        public Subscription<TMessageType> Subscribe<TMessageType>(Action<TMessageType> action)
        {
            Type type = typeof(TMessageType);
            var actionDetail = new Subscription<TMessageType>(action, this);

            if (!_subscribers.TryGetValue(type, out var actionList))
            {
                actionList = new List<object>();
                actionList.Add(actionDetail);
                _subscribers.Add(type, actionList);
            }
            else
            {
                actionList.Add(actionDetail);
            }

            return actionDetail;
        }

        public void UnSubscribe<TMessageType>(Subscription<TMessageType> subscription)
        {
            Type type = typeof(TMessageType);
            if (_subscribers.TryGetValue(type, out var list))
            {
                list.Remove(subscription);
            }
        }
    }
}
