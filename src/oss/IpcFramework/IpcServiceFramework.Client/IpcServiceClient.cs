﻿#if !DISABLE_DYNAMIC_CODE_GENERATION
using Castle.DynamicProxy;
#endif
using JKang.IpcServiceFramework.IO;
using JKang.IpcServiceFramework.Services;
using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;


namespace JKang.IpcServiceFramework
{
    public abstract class IpcServiceClient<TInterface>
        where TInterface : class
    {

#if !DISABLE_DYNAMIC_CODE_GENERATION
        private static readonly ProxyGenerator _proxyGenerator = new ProxyGenerator();
#endif
        private readonly IIpcMessageSerializer _serializer;
        private readonly IValueConverter _converter;

        protected IpcServiceClient(
            IIpcMessageSerializer serializer,
            IValueConverter converter)
        {
            _serializer = serializer;
            _converter = converter;
        }

        public async Task<TResult> InvokeAsync<TResult>(IpcRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            IpcResponse response = await GetResponseAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Succeed)
            {
                if (_converter.TryConvert(response.Data, typeof(TResult), out object @return))
                {
                    return (TResult)@return;
                }
                else
                {
                    throw new InvalidOperationException($"Unable to convert returned value to '{typeof(TResult).Name}'.");
                }
            }
            else
            {
                throw ThrowFailedResposeException(response);
            }
        }

        public async Task InvokeAsync(IpcRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            IpcResponse response = await GetResponseAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Succeed)
            {
                return;
            }
            else
            {
                throw ThrowFailedResposeException(response);
            }
        }

#if !DISABLE_DYNAMIC_CODE_GENERATION
        public async Task InvokeAsync(Expression<Action<TInterface>> exp,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            IpcRequest request = GetRequest(exp, new MyInterceptor());
            IpcResponse response = await GetResponseAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Succeed)
            {
                return;
            }
            else
            {
                throw ThrowFailedResposeException(response);
            }
        }

        public async Task<TResult> InvokeAsync<TResult>(Expression<Func<TInterface, TResult>> exp,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            IpcRequest request = GetRequest(exp, new MyInterceptor<TResult>());
            IpcResponse response = await GetResponseAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Succeed)
            {
                if (_converter.TryConvert(response.Data, typeof(TResult), out object @return))
                {
                    return (TResult)@return;
                }
                else
                {
                    throw new InvalidOperationException($"Unable to convert returned value to '{typeof(TResult).Name}'.");
                }
            }
            else
            {
                throw ThrowFailedResposeException(response);
            }
        }

        public async Task InvokeAsync(Expression<Func<TInterface, Task>> exp,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            IpcRequest request = GetRequest(exp, new MyInterceptor<Task>());
            IpcResponse response = await GetResponseAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Succeed)
            {
                return;
            }
            else
            {
                throw ThrowFailedResposeException(response);
            }
        }

        public async Task<TResult> InvokeAsync<TResult>(Expression<Func<TInterface, Task<TResult>>> exp,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            IpcRequest request = GetRequest(exp, new MyInterceptor<Task<TResult>>());
            IpcResponse response = await GetResponseAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Succeed)
            {
                if (_converter.TryConvert(response.Data, typeof(TResult), out object @return))
                {
                    return (TResult)@return;
                }
                else
                {
                    throw new InvalidOperationException($"Unable to convert returned value to '{typeof(TResult).Name}'.");
                }
            }
            else
            {
                throw ThrowFailedResposeException(response);
            }
        }

        private static IpcRequest GetRequest(Expression exp, MyInterceptor interceptor)
        {
            if (!(exp is LambdaExpression lamdaExp))
            {
                throw new ArgumentException("Only support lamda expresion, ex: x => x.GetData(a, b)");
            }

            if (!(lamdaExp.Body is MethodCallExpression methodCallExp))
            {
                throw new ArgumentException("Only support calling method, ex: x => x.GetData(a, b)");
            }

            TInterface proxy = _proxyGenerator.CreateInterfaceProxyWithoutTarget<TInterface>(interceptor);
            Delegate @delegate = lamdaExp.Compile();
            @delegate.DynamicInvoke(proxy);

            return new IpcRequest
            {
                MethodName = interceptor.LastInvocation.Method.Name,
                Parameters = interceptor.LastInvocation.Arguments,

                ParameterTypes = interceptor.LastInvocation.Method.GetParameters()
                              .Select(p => p.ParameterType.FullName)
                              .ToArray(),

                ParameterAssemblyNames = interceptor.LastInvocation.Method.GetParameters()
                              .Select(p => p.ParameterType.Assembly.GetName().Name)
                              .ToArray(),


                GenericArguments = interceptor.LastInvocation.GenericArguments,
            };
        }
#endif

        private Exception ThrowFailedResposeException(IpcResponse response)
        {
            // Respose was a failure. Throw the exception if there is one to throw
            object exception = null;
            if (response.Data != null)
            {
                _converter.TryConvert(response.Data, response.ExceptionType, out exception);
            }

            if (exception != null)
            {
                return (exception as Exception);
            }
            else
            {
                return new InvalidOperationException(response.Failure);
            }
        }

        protected abstract Task<Stream> ConnectToServerAsync(CancellationToken cancellationToken);

        private async Task<IpcResponse> GetResponseAsync(IpcRequest request, CancellationToken cancellationToken)
        {
            using (Stream client = await ConnectToServerAsync(cancellationToken).ConfigureAwait(false))
            using (var writer = new IpcWriter(client, _serializer, leaveOpen: true))
            using (var reader = new IpcReader(client, _serializer, leaveOpen: true))
            {
                // send request
                await writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);

                // receive response
                return await reader.ReadIpcResponseAsync(cancellationToken).ConfigureAwait(false);
            }
        }

#if !DISABLE_DYNAMIC_CODE_GENERATION
        private class MyInterceptor : IInterceptor
        {
            public IInvocation LastInvocation { get; private set; }

            public virtual void Intercept(IInvocation invocation)
            {
                LastInvocation = invocation;
            }
        }

        private class MyInterceptor<TResult> : MyInterceptor
        {
            public override void Intercept(IInvocation invocation)
            {
                base.Intercept(invocation);
                invocation.ReturnValue = default(TResult);
            }
        }
#endif
    }
}
