module App

open Elmish
open Elmish.React
open Elmish.HMR

Program.mkProgram State.init State.update State.view
|> Program.withSubscription Subscriptions.subscribe
|> Program.withReactSynchronous "root"
|> Program.run
