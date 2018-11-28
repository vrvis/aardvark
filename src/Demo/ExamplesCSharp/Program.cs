/* 

The purpose of this program is to demonstrate how to use the aardvark base libraries in C#.
C# and F# both compile to IL and therefore compose to some degree, i.e. libraries can be used
in both directions.
Aardvark.Base is somewhat special because it makes heavy use of F# features which have no direct
C# equivalent (except for boilderplate). 
Additionally, our C# interfaces for F# datatypes often lack convenience and C# idiomatic API (
(are not always as complete as the real implementation).
One goal of this file is to show the very basic interaction techniques and motivate developers to
contribute to the convenience layers to C#.

PS.: please help to improve this tutorial (typos, non-english etc)
*/

using Aardvark.Base.Incremental;
using Aardvark.Base.Incremental.CSharp;
using System;
using System.Linq;
using static Aardvark.Base.CSharpInterop;

namespace ExamplesCSharp
{
    class Program
    {
        static void Main(string[] args)
        {
            Aardvark.Base.IL.TypeBuilderTest.run();
            Environment.Exit(0);

            #region Basic Mod usage

            // Create a modref cell. can be changed via side effects
            var input = Mod.Init(10);

            var output = input.Map(x => x * 2);

            Console.WriteLine($"output was: {output}");
            // Prints: output was Aardvark.Base.Incremental.ModModule+MapMod`2[System.Int32,System.Int32]
            // not what we expected. Since mods are lazy and tostring does not force them we need
            // to pull the value out of it.

            Console.WriteLine($"output was: {output.GetValue()}"); // F# equivalent: Mod.force : IMod<'a> -> 'a
            // output was: 20

            using (Adaptive.Transaction)
            {
                input.Value = 20;
            }

            Console.WriteLine($"output was: {output.GetValue()}");
            // output was: 40

            // semantically, output now dependens on input declaratively. 
            // the dependency graph looks like:
            //   
            //       (x) => x * 2
            // input ----------------> output
            // mods are nodes, and the edges are annotated with transition functions.

            // Users of Rx might see the pattern. outputs is an observer, while input is observable.
            // so the systems are equivalent ?! but mod must be better - otherwise this tutorial is useless right?

            #endregion


#if false
            #region Obserable semantics

            // A modref could be an observable which never ends and has an initial value.
            var inputObs = new Subject<int>();
            inputObs.OnNext(10);
            var outputObs = inputObs.Select(x => x * 2);

            var globalStore = 0;
            var sideEffectToObserve = outputObs.Subscribe(x => globalStore = x);

            Console.WriteLine($"outputObs was: {globalStore}");
            // outputObs was: 0
            // unexpected? no. this is due to subscription semantics.
            // Note: ReplaySubject instead of subject has right semantics although i have doubts about memory leaks.

            inputObs.OnNext(10);
            Console.WriteLine($"outputObs was: {globalStore}");
            // outputObs was: 20

            inputObs.OnNext(20);
            Console.WriteLine($"outputObs was: {globalStore}");
            // outputObs was: 40


            // what happens if we have data flow graphs with sinks (2 ingoing edges conceptually)

            var inputA = new Subject<int>();
            var inputB = new Subject<int>();

            var reexCount = 0;

            inputA.Merge(inputB).Subscribe(x =>
            {
                reexCount++;
                Console.WriteLine($"a+b was: {x}");
            });

            inputA.OnNext(1);
            inputB.OnNext(2);
            Console.WriteLine($"reexCount was: {reexCount}");
            // reexCount was: 2

            // did you expect 2? of course. this is the semantics of merge.

            // what iff we only want to have a batch change, say, change 2 inputs simultationusly? 
            // then we need different semantics. 
            var inputA3 = new Subject<int>();
            var inputB3 = new Subject<int>();

            reexCount = 0;
            inputA3.SelectMany(a =>
                inputB3.Select(b =>
                {
                    reexCount++;
                    return a + b;
                }
                )
            ).Subscribe(r =>
                Console.WriteLine($"result was: {r} reexCount was: {reexCount}")
            );

            inputA3.OnNext(1);
            inputB3.OnNext(2);
            // result was: 3 reexCount was: 1

            // If we switch to replay subject we get 2 again...
            var inputA2 = new ReplaySubject<int>();
            var inputB2 = new ReplaySubject<int>();

            reexCount = 0;
            inputA2.SelectMany(a =>
                inputB2.Select(b =>
                {
                    reexCount++;
                    return a + b;
                }
                )
            ).Subscribe(r =>
                Console.WriteLine($"result was: {r} reexCount was: {reexCount}")
            );

            inputA2.OnNext(1);
            inputB2.OnNext(2);
            // result was: 3 reexCount was: 2

            // Let us see how this looks like in Mod:
            reexCount = 0;
            var inputAM = Mod.Init(1);
            var inputBM = Mod.Init(2);
            var aPlusB = inputAM.Map(inputBM, 
                (a, b) => {
                    reexCount++;
                    return a + b;
                });

            Console.WriteLine($"mod,a+b was: {aPlusB.GetValue()}, reexCount: {reexCount}");
            // mod,a+b was: 3, reexCount: 1

            // special note: Select2 was not defined at the time or writing of this tutorial, but
            // it is available in F#. How could we access the F# verion?
            var aPlusB2 = ModModule.map2(FSharpFuncUtil.Create<int,int,int>((a, b) => a + b), inputAM, inputBM);
            // steps required: 
            // (1) F# map2 is defined in Mod module. usage Mod.map2 (+) a b
            // so we need this map module. by convention, modules with colliding type names
            // are exported with the module suffix. so the function lives in ModModule.
            // (2) our f# function wants a f# function and not an instance of type System.Func. use a conversion
            // (3) C# has no real type inference, so most of the time you'll need to annotate stuff.
            // that is - we just used a f# function with no c# friendly interface in c#.
            Console.WriteLine($"mod,a+b was: {aPlusB2.GetValue()}, reexCount: {reexCount}");

            reexCount = 0;
            using (Adaptive.Transaction)
            {
                inputAM.Value = 20;
                inputBM.Value = 30;
            }

            Console.WriteLine($"mod,a+b was: {aPlusB.GetValue()}, reexCount: {reexCount}");
            // mod,a+b was: 50, reexCount: 1

            // so we have batch changes in the mod system. but this is cheating, right?
            // because we used an optimized combinator which does this, right?
            // we can do a low level implementation instead.

            var aPlusBBind = inputAM.Bind(a => inputBM.Map(b =>
            {
                reexCount++;
                return a + b;
            }));

            reexCount = 0;
            using (Adaptive.Transaction)
            {
                inputAM.Value = 20;
                inputBM.Value = 30;
            }

            Console.WriteLine($"modbind,a+b was: {aPlusBBind.GetValue()}, reexCount: {reexCount}");
            // modbind,a+b was: 50, reexCount: 1
            // interesting. also here we have tight reexecution count.

            reexCount = 0;
            using (Adaptive.Transaction)
            {
                inputAM.Value = 20;
                inputBM.Value = 30;
            }

            Console.WriteLine($"modbind2,a+b was: {aPlusBBind.GetValue()}, reexCount: {reexCount}");
            // modbind2,a + b was: 50, reexCount:0
            // aha - we have reexCount=0 because the change was a pseudo change (values changed to old values)

            /*
            So what is the result of this analysis? Rx has precise semantics. You get what you want. But
            you need to know how you want it and there are many solutions.
            So Mod is the same as observable, but can do less because we do not have precise control about
            reexecution semantics (although semantics seems to be nice, right)?
            */

            // One could use the mod system as strange implementation of observable of course.
            var inputEvil = Mod.Init(10);
            var outputEvil = Mod.Init(0);
            var sub = inputEvil.UnsafeRegisterCallbackNoGcRoot(i =>
            {
                using (Adaptive.Transaction)
                {
                    outputEvil.Value = i * 2;
                }
            });

            using (Adaptive.Transaction)
            {
                inputEvil.Value = 20;
            }
            Console.WriteLine($"evilCallback: {outputEvil.GetValue()}");
            // evilCallback: 40

            /* so this works. but this is evil.
            /* this code has no dependency graph. all is modelled via side effects.
            /* The following list sponsored by gh gives some reasons against callbacks:
            1) The Mod-system is capable of handling those callbacks but their cost is massive compared to Rx
            2) Callbacks tend to keep their closure alive(causing memory leaks)
            3) Callbacks are hard to debug
            4) Eager evaluation wastes time (by design and especially when using the mod-system)
            5) Concurrency is not controlled in any way
            6) Callers of transact suddenly block until their callbacks finish(deadlock scenarios etc.)
            7) Rx was already invented (so why exploit our system to simulate it) */

            #endregion

            /*
            So finally, the answer is: NO
            Rx and Mod is something completly different. 
            - Mod is lazy / Obs pushes values to their subscribers immediately
            - Mods have batch changes intrinsic to the system / in Obs semantics depends on combinators
            - Mods build dependency graphs and try to reduce recomputation overhead / Obs don't care about algorithmic complexity, Obs cannot be used to implement adaptive data structures
            - Mods have on goal: make the outputs consistent iff they are demaned. / Obs populates all values according to the combinators used (e.g. merge)
            */

            // As a result there are many things which do not fit to observables, others do not fit to mods.

            // Given this list, Mods are particularly not immediately usable for tracking changes manually.
            var resendOverNetwork = Mod.Init(false);
            inputEvil.UnsafeRegisterCallbackNoGcRoot(s =>
            {
                using (Adaptive.Transaction)
                {
                    resendOverNetwork.Value = true;
                }
            }
            );
            resendOverNetwork.UnsafeRegisterCallbackNoGcRoot(_ =>
            {
                inputEvil.GetValue().ToString(); // send new contents of inputEvil
            });

            // UnsafeRegisterCallbackNoGcRoot is a hack in the system to allow unproblematic side effects
            // to be coupled with reecution. The exeuction of order of callbacks, especially callbacks
            // which run callbacks via transactions is highly unspecified. In fact
            // UnsafeRegisterCallbackNoGcRoot has no defined semantics.

            // One little note: (in the current implementation) all callbacks are executed eventuallly, but maybe to often.
            // This is very comparable to LINQ mixed with side effects. LINQ has lazy evaluation and using
            // side effects inside is just nonesense. Fortunately LINQ has no public cheat API --- one cannot
            // simply "side-effect" elements into an existing enumerable sequence 
            // (ok we can but peoply luckily rarely mix side effects with linq because meijer said it is evil [1]).
            // [1] https://www.youtube.com/watch?v=UuamC0T3hv8 
            // Of course we could restrict the mod API (by removing callbacks), but
            // we still wanted ways to do unsafe stuff (in less than 0.01% of the code). 
            // At this point we shall mention that the complete rendering backend works without callbacks,
            // but rendering things could be as well considered as rather imperative problem.
            // If you really want to attach callbacks to some mod, maybe either:
            // (1) the problem does not fit the declarative incremental computation modlel
            // (2) the problem fits, but some parts of the current solution are not delcarative (either because of (1), because of hacks or other non-declarative parts)

            /* You know might think: "Why all this complexity. Why not just use Observables where
            appropriate and ad hoc techniques when they not work as nice as they should. I pretty
            lived quite good the last years without a mod system and this stuff".
            Cases in which the mod system is a good fit can greatly benefit from the usage of mods.
            The changes are not local, but having a mod system at hand completely changes the way
            on can structure programs. In fact, i think programming with mods is a completely different
            paradigm. So Take your time and help us making the libraries better.
            */
#endif
        }
    }
}

