﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Mvc.Routing
{
    internal class ControllerEndpointFactory
    {
        private readonly ApplicationModelFactory _factory;
        private readonly RoutePatternTransformer _patternTransformer;

        public ControllerEndpointFactory(ApplicationModelFactory factory, RoutePatternTransformer patternTransformer)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            if (patternTransformer == null)
            {
                throw new ArgumentNullException(nameof(patternTransformer));
            }

            _factory = factory;
            _patternTransformer = patternTransformer;
        }

        public List<Endpoint> CreateEndpoints(
            IEnumerable<TypeInfo> types,
            IReadOnlyList<ConventionalRouteEntry> routes,
            IReadOnlyList<Action<EndpointModel>> conventions)
        {
            var endpoints = new List<Endpoint>();

            var controllerTypes = types.Where(ControllerFeatureProvider.DefaultIsController);
            var applicationModel = _factory.CreateApplicationModel(controllerTypes);
            
            var tuples = ApplicationModelFactory.Flatten(applicationModel, Map);
            for (var i = 0; i < tuples.Count; i++)
            {
                foreach (var model in CreateModels(tuples[i], routes))
                {
                    for (var k = 0; k < conventions.Count; k++)
                    {
                        conventions[k](model);
                    }

                    endpoints.Add(model.Build());
                }
            }

            return endpoints;
        }

        private static (ApplicationModel, ControllerModel, ActionModel, SelectorModel) Map(ApplicationModel application, ControllerModel controller, ActionModel action, SelectorModel selector)
        {
            return (application, controller, action, selector);
        }

        private IEnumerable<ControllerActionEndpointModel> CreateModels(
            (ApplicationModel, ControllerModel, ActionModel, SelectorModel) tuple,
            IReadOnlyList<ConventionalRouteEntry> routes)
        {
            var (application, controller, action, selector) = tuple;

            var values = new RouteValueDictionary(controller.RouteValues);
            foreach (var kvp in action.RouteValues)
            {
                values[kvp.Key] = kvp.Value;
            }

            values.TryAdd("action", action.ActionName);
            values.TryAdd("controller", controller.ControllerName);

            // We need to transform data in the selector and conventional route mappings
            // into a route patterns. After this this block we will ignore the attribute route info
            // because we've already processed it.
            var patterns = new List<(RoutePattern pattern, AttributeRouteModel info, ConventionalRouteEntry route)>();
            if (selector.AttributeRouteModel == null)
            {
                // This is conventionally routed. There should be one model per *matching* conventional route.
                for (var i = 0; i < routes.Count; i++)
                {
                    var route = routes[i];
                    var pattern = _patternTransformer.SubstituteRequiredValues(route.Pattern, values);

                    // It's OK for pattern to be null - that means that the route cannot match this action.
                    if (pattern != null)
                    {
                        patterns.Add((pattern, null, route));
                    }
                }
            }
            else
            {
                // This is attribute routed. There should only be one model created.

                // Currently there is no supported way for user-code to specify additonal defaults and
                // constraints for an attribute routed action outside of the template.
                var pattern = RoutePatternFactory.Parse(selector.AttributeRouteModel.Template, values, null, values);
                patterns.Add((pattern, selector.AttributeRouteModel, null));
            }

            for (var i = 0; i < patterns.Count; i++)
            {
                var pattern = patterns[i];

                var model = new ControllerActionEndpointModel(controller.ControllerType, controller.ControllerName, action.ActionMethod, action.ActionName)
                {
                    DisplayName = ControllerActionDescriptor.GetDefaultDisplayName(controller.ControllerType, action.ActionMethod),
                    Order = pattern.info?.Order ?? pattern.route?.Order ?? 0,
                    RequestDelegate = null, // TODO
                    RoutePattern = pattern.pattern,
                };
                
                model.RequestDelegate = (context) =>
                {
                    var routeData = context.GetRouteData();
                    var endpoint = context.Features.Get<IEndpointFeature>().Endpoint;

                    var actionContext = new ActionContext(context, routeData, endpoint.Metadata.GetMetadata<ActionDescriptor>());

                    var invokerFactory = context.RequestServices.GetRequiredService<MvcEndpointInvokerFactory>();
                    var invoker = invokerFactory.CreateInvoker(actionContext);
                    return invoker.InvokeAsync();
                };

                for (var j = 0; j < controller.ControllerProperties.Count; j++)
                {
                    var property = controller.ControllerProperties[j];
                    if (property.BindingInfo != null)
                    {
                        model.Parameters.Add(new ControllerActionParameterModel(property.PropertyInfo)
                        {
                            BindingInfo = property.BindingInfo,
                            Name = property.PropertyName,
                        });
                    }
                }

                for (var j = 0; j < action.Parameters.Count; j++)
                {
                    var parameter = action.Parameters[j];
                    model.Parameters.Add(new ControllerActionParameterModel(parameter.ParameterInfo)
                    {
                        BindingInfo = parameter.BindingInfo,
                        Name = parameter.ParameterName,
                    });
                }

                var filters =
                    action.Filters.Select(f => new FilterDescriptor(f, FilterScope.Action))
                    .Concat(controller.Filters.Select(f => new FilterDescriptor(f, FilterScope.Controller)))
                    .Concat(application.Filters.Select(f => new FilterDescriptor(f, FilterScope.Global)))
                    .OrderBy(d => d, FilterDescriptorOrderComparer.Comparer);
                foreach (var filter in filters)
                {
                    model.Filters.Add(filter);
                }

                var apiDescriptionData = ApiDescriptionActionData.Create(application, controller, action, selector);
                if (apiDescriptionData != null)
                {
                    model.Properties[typeof(ApiDescriptionActionData)] = apiDescriptionData;
                }

                model.Properties[typeof(IList<IActionConstraintMetadata>)] = selector.ActionConstraints;

                foreach (var metadata in selector.EndpointMetadata)
                {
                    model.Metadata.Add(metadata);
                }

                if (pattern.info != null && pattern.info.SuppressLinkGeneration)
                {
                    model.Metadata.Add(new SuppressLinkGenerationMetadata());
                }

                if (pattern.info != null && pattern.info.SuppressPathMatching)
                {
                    model.Metadata.Add(new SuppressMatchingMetadata());
                }

                foreach (var item in application.Properties)
                {
                    model.Properties[item.Key] = item.Value;
                }

                foreach (var item in controller.Properties)
                {
                    model.Properties[item.Key] = item.Value;
                }

                foreach (var item in action.Properties)
                {
                    model.Properties[item.Key] = item.Value;
                }

                yield return model;
            }
        }

    }
}
