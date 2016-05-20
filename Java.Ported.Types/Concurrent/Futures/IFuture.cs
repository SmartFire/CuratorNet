﻿using System;

namespace Org.Apache.Java.Types.Concurrent.Futures
{
    /// <summary>
    /// A Future represents the result of an asynchronous
    /// computation.Methods are provided to check if the computation is
    /// complete, to wait for its completion, and to retrieve the result of
    /// the computation.  The result can only be retrieved using method
    /// "get"
    /// when the computation has completed, blocking if
    /// necessary until it is ready.Cancellation is performed by the
    /// "cancel" method.Additional methods are provided to
    /// determine if the task completed normally or was cancelled.Once a
    /// computation has completed, the computation cannot be cancelled.
    /// If you would like to use a "Future" for the sake
    /// of cancellability but not provide a usable result, you can
    /// declare types of the form "Future<T>"
    /// and
    /// return null as a result of the underlying task.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IFuture<out T>
    {
        bool cancel();

        bool isCancelled();

        bool isDone();

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        T get();
        
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        T get(int timeoutMs);
    }
}
