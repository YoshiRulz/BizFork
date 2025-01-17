{ pkgs ? import <nixpkgs> {}
, lib ? pkgs.lib
, mkShell ? pkgs.mkShell
, git-cola ? pkgs.git-cola
, git ? pkgs.gitMinimal
, nano ? pkgs.nano
, debugDotnetHostCrashes ? false # forwarded to Dist/launch-scripts.nix
, debugPInvokes ? false # forwarded to Dist/launch-scripts.nix
, forNixOS ? true
, useKate ? false
, useNanoAndCola ? false
, useVSCode ? false
}: let
	# thinking of exposing pre-configured IDEs from `default.nix` so they're available here
	avail = import ./. { inherit debugDotnetHostCrashes debugPInvokes forNixOS; };
	f = drv: mkShell {
		packages = [ git ]
			++ lib.optionals useNanoAndCola [ git-cola nano ]
			++ lib.optionals useKate [] #TODO
			++ lib.optionals useVSCode [] #TODO https://devblogs.microsoft.com/dotnet/csharp-dev-kit-now-generally-available/ https://learn.microsoft.com/en-us/training/modules/implement-visual-studio-code-debugging-tools/
			;
		inputsFrom = [ drv ];
		shellHook = ''
			export BIZHAWKBUILD_HOME='${builtins.toString ./.}'
			export BIZHAWK_HOME="$BIZHAWKBUILD_HOME/output"
			alias discohawk-monort-local='${avail.launchScriptsForLocalBuild.discohawk}'
			alias emuhawk-monort-local='${avail.launchScriptsForLocalBuild.emuhawk}'
			pfx="$(realpath --relative-to="$PWD" "$BIZHAWKBUILD_HOME")/"
			if [ "$pfx" = "./" ]; then pfx=""; fi
			printf "%s\n%s\n" \
				"Run ''${pfx}Dist/Build{Debug,Release}.sh to build the solution. You may need to clean up with ''${pfx}Dist/CleanupBuildOutputDirs.sh." \
				"Once built, running {discohawk,emuhawk}-monort-local will pull from ''${pfx}output/* and use Mono from Nixpkgs."
		'';
	};
	shells = lib.pipe avail [
		(lib.mapAttrs (name: drv: if lib.hasPrefix "bizhawkAssemblies-" name then drv else drv.assemblies or null))
		(lib.filterAttrs (_: drv: drv != null))
		(lib.mapAttrs (_: asms: lib.traceIf (lib.hasSuffix "-bin" asms.name) "the attr specified packages BizHawk from release artifacts; some builddeps may be missing from this shell"
			f asms))
	];
in shells // shells.emuhawk-latest
