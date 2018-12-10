// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder
{
    public static class MvcEndpointRouteBuilderExtensions
    {
        // TODO: Make this support multiple frameworks
        public static IEndpointConventionBuilder MapApplication(this IEndpointRouteBuilder routeBuilder)
        {
            if (routeBuilder == null)
            {
                throw new ArgumentNullException(nameof(routeBuilder));
            }
            
            var dataSource = GetOrCreateDataSource(routeBuilder);
            return dataSource.AddApplicationAssemblies();
        }

        // TODO: Make this support multiple frameworks
        public static IEndpointConventionBuilder MapAssembly<TContainingType>(this IEndpointRouteBuilder routeBuilder)
        {
            if (routeBuilder == null)
            {
                throw new ArgumentNullException(nameof(routeBuilder));
            }

            var dataSource = GetOrCreateDataSource(routeBuilder);
            return dataSource.AddAssembly(typeof(TContainingType).Assembly);
        }

        // TODO: Make this support multiple frameworks
        public static IEndpointConventionBuilder MapAssembly(this IEndpointRouteBuilder routeBuilder, Type containingType)
        {
            if (routeBuilder == null)
            {
                throw new ArgumentNullException(nameof(routeBuilder));
            }

            if (containingType == null)
            {
                throw new ArgumentNullException(nameof(containingType));
            }

            var dataSource = GetOrCreateDataSource(routeBuilder);
            return dataSource.AddAssembly(containingType.Assembly);
        }

        // TODO: Make this support multiple frameworks
        public static IEndpointConventionBuilder MapAssembly(this IEndpointRouteBuilder routeBuilder, Assembly assembly)
        {
            if (routeBuilder == null)
            {
                throw new ArgumentNullException(nameof(routeBuilder));
            }

            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            var dataSource = GetOrCreateDataSource(routeBuilder);
            return dataSource.AddAssembly(assembly);
        }

        public static IEndpointConventionBuilder MapControllerType<TController>(this IEndpointRouteBuilder routeBuilder)
        {
            if (routeBuilder == null)
            {
                throw new ArgumentNullException(nameof(routeBuilder));
            }

            var dataSource = GetOrCreateDataSource(routeBuilder);
            return dataSource.AddType(typeof(TController));
        }

        public static IEndpointConventionBuilder MapControllerType(this IEndpointRouteBuilder routeBuilder, Type controllerType)
        {
            if (routeBuilder == null)
            {
                throw new ArgumentNullException(nameof(routeBuilder));
            }

            if (controllerType == null)
            {
                throw new ArgumentNullException(nameof(controllerType));
            }

            var dataSource = GetOrCreateDataSource(routeBuilder);
            return dataSource.AddType(controllerType);
        }

        public static void MapControllerRoute(
            this IEndpointRouteBuilder routeBuilder,
            string name,
            string template,
            object defaults = null,
            object constraints = null,
            object dataTokens = null)
        {
            var dataSource = GetOrCreateDataSource(routeBuilder);
            dataSource.AddConventionalRoute(new ConventionalRouteEntry(
                name, 
                template,
                new RouteValueDictionary(defaults),
                new RouteValueDictionary(constraints),
                new RouteValueDictionary(dataTokens),
                dataSource.NextConventionalRouteOrder));
        }

        private static ControllerEndpointDataSource GetOrCreateDataSource(IEndpointRouteBuilder builder)
        {
            var controllerDataSource = builder.DataSources.OfType<ControllerEndpointDataSource>().SingleOrDefault();
            if (controllerDataSource == null)
            {
                controllerDataSource = builder.ServiceProvider.GetRequiredService<ControllerEndpointDataSource>();
                builder.DataSources.Add(controllerDataSource);
            }

            return controllerDataSource;
        }
    }
}
