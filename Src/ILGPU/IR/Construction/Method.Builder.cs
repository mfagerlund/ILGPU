// -----------------------------------------------------------------------------
//                                    ILGPU
//                     Copyright (c) 2016-2019 Marcel Koester
//                                www.ilgpu.net
//
// File: Method.Builder.cs
//
// This file is part of ILGPU and is distributed under the University of
// Illinois Open Source License. See LICENSE.txt for details
// -----------------------------------------------------------------------------

using ILGPU.Frontend.DebugInformation;
using ILGPU.IR.Analyses;
using ILGPU.IR.Construction;
using ILGPU.IR.Types;
using ILGPU.IR.Values;
using ILGPU.Util;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ILGPU.IR
{
    partial class Method
    {
        /// <summary>
        /// A parameter builder for use in combination with the
        /// <see cref="ParameterCollection.Builder{TParameterBuilder}"/> object.
        /// </summary>
        public readonly struct ParameterBuilder : ParameterCollection.IParameterBuilder
        {
            /// <summary>
            /// Constructs a new parameter builder.
            /// </summary>
            /// <param name="builder">The parent builder.</param>
            public ParameterBuilder(Builder builder)
            {
                Debug.Assert(builder != null, "Invalid builder");
                Builder = builder;
            }

            /// <summary>
            /// Returns the current basic block.
            /// </summary>
            public Builder Builder { get; }

            /// <summary cref="ParameterCollection.IParameterBuilder.Add(Parameter)"/>
            public void Add(Parameter parameter) { }

            /// <summary cref="ParameterCollection.IParameterBuilder.Remove(Parameter)"/>
            public void Remove(Parameter parameter) { }

            /// <summary cref="ParameterCollection.IParameterBuilder.CreateParameter(TypeNode, string)"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Parameter CreateParameter(TypeNode type, string name)
            {
                var param = new Parameter(Builder.Method, type, name)
                {
                    SequencePoint = Builder.SequencePoint
                };
                Builder.Context.Create(param);
                return param;
            }
        }

        /// <summary>
        /// Determines whether a block argument is still required.
        /// </summary>
        private readonly struct ArgumentMapper : BranchTarget.IArgumentMapper
        {
            /// <summary>
            /// Constructs a new argument mapper.
            /// </summary>
            /// <param name="parent">The parent builder.</param>
            public ArgumentMapper(Builder parent)
            {
                Parent = parent;
            }

            /// <summary>
            /// Returns the parent method builder.
            /// </summary>
            public Builder Parent { get; }

            /// <summary cref="BranchTarget.IArgumentMapper.CanMapBlockArgument(BranchTarget, int)"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool CanMapBlockArgument(BranchTarget target, int argumentIndex)
            {
                if (!target.TargetBlock.TryGetBuilder(out var builder))
                    return true;
                return !builder.Parameters[argumentIndex].IsReplaced;
            }
        }

        /// <summary>
        /// A builder to build methods.
        /// </summary>
        public sealed class Builder : DisposeBase, IMethodMappingObject
        {
            #region Instance

            /// <summary>
            /// All created basic block builders.
            /// </summary>
            private readonly List<BasicBlock.Builder> basicBlockBuilders =
                new List<BasicBlock.Builder>();

            /// <summary>
            /// Constructs a new method builder.
            /// </summary>
            /// <param name="method">The parent method.</param>
            internal Builder(Method method)
            {
                Method = method;
                Parameters = method.Parameters.ToBuilder(new ParameterBuilder(this));
                EnableDebugInformation = Context.HasFlags(
                    ContextFlags.EnableDebugInformation);
            }

            #endregion

            #region Properties

            /// <summary>
            /// Returns the associated IR context.
            /// </summary>
            public IRContext Context => Method.Context;

            /// <summary>
            /// Retruns true if debbug information is enabled.
            /// </summary>
            public bool EnableDebugInformation { get; }

            /// <summary>
            /// Returns the associated method.
            /// </summary>
            public Method Method { get; }

            /// <summary>
            /// Gets or sets the current entry block.
            /// </summary>
            public BasicBlock EntryBlock
            {
                get => Method.EntryBlock;
                set => Method.EntryBlock = value;
            }

            /// <summary>
            /// Returns the associated function handle.
            /// </summary>
            public MethodHandle Handle => Method.Handle;

            /// <summary>
            /// Returns the original source method (may be null).
            /// </summary>
            public MethodBase Source => Method.Source;

            /// <summary>
            /// Returns the current parameter builder.
            /// </summary>
            public ParameterCollection.Builder<ParameterBuilder> Parameters { get; }

            /// <summary>
            /// Returns the associated basic block builder.
            /// </summary>
            /// <param name="basicBlock">The basic block to resolve the builder for.</param>
            /// <returns>The resolved basic block builder.</returns>
            public BasicBlock.Builder this[BasicBlock basicBlock]
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get
                {
                    Debug.Assert(basicBlock != null, "Invalid basic block to lookup");
                    Debug.Assert(basicBlock.Method == Method, "Invalid associated function");
                    if (basicBlock.GetOrCreateBuilder(
                        this,
                        out BasicBlock.Builder basicBlockBuilder))
                        basicBlockBuilders.Add(basicBlockBuilder);
                    return basicBlockBuilder;
                }
            }

            /// <summary>
            /// Gets or sets the current sequence point (if any).
            /// </summary>
            public SequencePoint SequencePoint { get; set; }

            #endregion

            #region Methods

            /// <summary>
            /// Setups the initial sequence point by binding the method's and
            /// the entry block's sequence points.
            /// </summary>
            /// <param name="sequencePoint">The sequence point to setup.</param>
            public void SetupInitialSequencePoint(SequencePoint sequencePoint)
            {
                SequencePoint = sequencePoint;
                Method.SequencePoint = sequencePoint;
                if (EntryBlock != null)
                    EntryBlock.SequencePoint = sequencePoint;
            }

            /// <summary>
            /// Creates a new unique node marker.
            /// </summary>
            /// <returns>The new node marker.</returns>
            public NodeMarker NewNodeMarker() => Context.NewNodeMarker();

            /// <summary>
            /// Creates a new method scope with default flags.
            /// </summary>
            /// <returns>A new method scope.</returns>
            public Scope CreateScope() => Method.CreateScope();

            /// <summary>
            /// Creates a new method scope with custom flags.
            /// </summary>
            /// <param name="scopeFlags">The scope flags.</param>
            /// <returns>A new method scope.</returns>
            public Scope CreateScope(ScopeFlags scopeFlags) => Method.CreateScope(scopeFlags);

            /// <summary>
            /// Creates a new rebuilder that works on the given scope.
            /// </summary>
            /// <param name="parameterMapping">The target value of every parameter.</param>
            /// <param name="scope">The used scope.</param>
            /// <returns>The created rebuilder.</returns>
            public IRRebuilder CreateRebuilder(
                ParameterMapping parameterMapping,
                Scope scope) =>
                CreateRebuilder(parameterMapping, scope, default);

            /// <summary>
            /// Creates a new rebuilder that works on the given scope.
            /// </summary>
            /// <param name="parameterMapping">The target value of every parameter.</param>
            /// <param name="scope">The used scope.</param>
            /// <param name="methodMapping">The method mapping.</param>
            /// <returns>The created rebuilder.</returns>
            public IRRebuilder CreateRebuilder(
                ParameterMapping parameterMapping,
                Scope scope,
                MethodMapping methodMapping)
            {
                Debug.Assert(parameterMapping.Method == scope.Method, "Invalid parameter mapping");
                Debug.Assert(scope != null, "Invalid scope");
                Debug.Assert(scope.Method != Method, "Cannot rebuild a function into itself");

                return new IRRebuilder(
                    this,
                    parameterMapping,
                    scope,
                    methodMapping);
            }

            /// <summary>
            /// Creates a new entry block.
            /// </summary>
            /// <returns>The created entry block.</returns>
            public BasicBlock.Builder CreateEntryBlock()
            {
                var block = CreateBasicBlock("Entry");
                EntryBlock = block.BasicBlock;
                return block;
            }

            /// <summary>
            /// Creates a new basic block.
            /// </summary>
            /// <returns>The created basic block.</returns>
            public BasicBlock.Builder CreateBasicBlock() =>
                CreateBasicBlock(null);

            /// <summary>
            /// Creates a new basic block.
            /// </summary>
            /// <returns>The created basic block.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public BasicBlock.Builder CreateBasicBlock(string name)
            {
                var block = new BasicBlock(
                    Method,
                    name,
                    Context.Context.CreateNodeId())
                {
                    SequencePoint = SequencePoint
                };
                return this[block];
            }

            /// <summary>
            /// Creates an instantiated value.
            /// </summary>
            /// <param name="node">The node to create.</param>
            /// <returns>The created node.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal void Create(Value node)
            {
                // Register the node
                Context.Create(node);

                // Check for enabled debug information
                if (!EnableDebugInformation)
                    return;

                // Create sequence point information
                var currentSequencePoint = SequencePoint;
                if (!currentSequencePoint.IsValid)
                {
                    // Try to infer sequence points from all child nodes
                    foreach (var childNode in node.Nodes)
                    {
                        currentSequencePoint = SequencePoint.Merge(
                            currentSequencePoint,
                            childNode.SequencePoint);
                    }
                }

                // Store final sequence point
                node.SequencePoint = currentSequencePoint;
            }

            /// <summary>
            /// Declares a method.
            /// </summary>
            /// <param name="methodBase">The method base.</param>
            /// <param name="created">True, iff the method has been created.</param>
            /// <returns>The declared method.</returns>
            public Method DeclareMethod(
                MethodBase methodBase,
                out bool created) =>
                Context.Declare(methodBase, out created);

            /// <summary>
            /// Declares a method.
            /// </summary>
            /// <param name="declaration">The method declaration.</param>
            /// <param name="created">True, iff the method has been created.</param>
            /// <returns>The declared method.</returns>
            public Method DeclareMethod(
                in MethodDeclaration declaration,
                out bool created) =>
                Context.Declare(declaration, out created);

            #endregion

            #region IDisposable

            /// <summary cref="DisposeBase.Dispose(bool)"/>
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    // Dispose the parameter builder
                    Parameters.Dispose();

                    // Cleanup all basic block builders
                    foreach (var builder in basicBlockBuilders)
                        builder.MapChangedArguments(new ArgumentMapper(this));

                    // Dispose all basic block builders
                    foreach (var builder in basicBlockBuilders)
                        builder.Dispose();
                    Method.ReleaseBuilder(this);
                }
                base.Dispose(disposing);
            }

            #endregion

            #region Object

            /// <summary>
            /// Returns the string representation of the underlying function.
            /// </summary>
            /// <returns>The string representation of the underlying function.</returns>
            public override string ToString() => Method.ToString();

            #endregion
        }
    }
}
